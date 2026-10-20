using System.Text.Json;
using AutomatasFinal.Infrastructure.Data;
using AutomatasFinal.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Audit;

/// <summary>Consultas de solo lectura para el panel de auditoría (ADMIN).</summary>
public class AuditQueryService
{
    public const int MaxPageSize = 100;

    private readonly AppDbContext _db;

    public AuditQueryService(AppDbContext db)
    {
        _db = db;
    }

    public record LogFilter(
        string? Category,
        string? Action,
        Guid? UserId,
        bool? Success,
        DateTime? From,
        DateTime? To,
        string? Search,
        int Page,
        int PageSize);

    public async Task<object> GetLogsAsync(LogFilter filter)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query =
            from log in _db.AuditLogs.AsNoTracking()
            join actor in _db.Users on log.UserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            join target in _db.Users on log.TargetUserId equals target.Id into targets
            from target in targets.DefaultIfEmpty()
            select new { log, actor, target };

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var actions = AuditActions.ActionsOf(filter.Category.Trim().ToUpperInvariant());
            query = query.Where(x => actions.Contains(x.log.Action));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim().ToUpperInvariant();
            query = query.Where(x => x.log.Action == action);
        }

        if (filter.UserId is not null)
        {
            query = query.Where(x =>
                x.log.UserId == filter.UserId ||
                x.log.TargetUserId == filter.UserId);
        }

        if (filter.Success is not null)
        {
            query = query.Where(x => x.log.Success == filter.Success);
        }

        if (filter.From is not null)
        {
            var from = ToUtc(filter.From.Value);
            query = query.Where(x => x.log.CreatedAt >= from);
        }

        if (filter.To is not null)
        {
            var to = ToUtc(filter.To.Value);
            query = query.Where(x => x.log.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Se busca por correo/nickname de quien actuó o del afectado.
            var pattern = $"%{EscapeLike(filter.Search.Trim())}%";

            query = query.Where(x =>
                (x.actor != null &&
                 (EF.Functions.ILike(x.actor.Email, pattern) ||
                  EF.Functions.ILike(x.actor.Nickname, pattern))) ||
                (x.target != null &&
                 (EF.Functions.ILike(x.target.Email, pattern) ||
                  EF.Functions.ILike(x.target.Nickname, pattern))));
        }

        var total = await query.CountAsync();

        var rows = await query
            .OrderByDescending(x => x.log.CreatedAt)
            .ThenByDescending(x => x.log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.log.Id,
                x.log.CreatedAt,
                x.log.Action,
                x.log.Success,
                x.log.Details,
                x.log.IpAddress,
                x.log.UserAgent,
                Actor = x.actor == null
                    ? null
                    : new { x.actor.Id, x.actor.Nickname, x.actor.Email },
                Target = x.target == null
                    ? null
                    : new { x.target.Id, x.target.Nickname, x.target.Email }
            })
            .ToListAsync();

        var labels = AuditActions.All.ToDictionary(x => x.Action);

        var items = rows.Select(x => new
        {
            x.Id,
            x.CreatedAt,
            x.Action,
            Label = labels.TryGetValue(x.Action, out var info) ? info.Label : x.Action,
            Category = labels.TryGetValue(x.Action, out var info2) ? info2.Category : "OTRO",
            x.Success,
            Details = ParseDetails(x.Details),
            Ip = x.IpAddress?.ToString(),
            x.UserAgent,
            x.Actor,
            x.Target
        });

        return new { items, total, page, pageSize };
    }

    public async Task<object> GetUsersActivityAsync()
    {
        var now = DateTime.UtcNow;
        var onlineSince = now - SessionService.OnlineWindow;

        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(x => x.Nickname)
            .Select(u => new
            {
                u.Id,
                u.Nickname,
                u.Email,
                Role = u.Role.Code,
                u.IsActive,
                Deleted = u.DeletedAt != null,
                u.DeletedAt,
                u.LastLoginAt,
                u.CreatedAt,
                OpenSessions = u.Sessions.Count(s => s.RevokedAt == null && s.ExpiresAt > now),
                LastSeenAt = u.Sessions
                    .OrderByDescending(s => s.LastSeenAt)
                    .Select(s => (DateTime?)s.LastSeenAt)
                    .FirstOrDefault(),
                Online = u.Sessions.Any(s =>
                    s.RevokedAt == null &&
                    s.ExpiresAt > now &&
                    s.LastSeenAt >= onlineSince)
            })
            .ToListAsync();

        return users;
    }

    public async Task<object> GetSessionsAsync(bool onlyOpen, Guid? userId, int limit)
    {
        var now = DateTime.UtcNow;
        var onlineSince = now - SessionService.OnlineWindow;

        var query = _db.UserSessions.AsNoTracking();

        if (onlyOpen)
        {
            query = query.Where(x => x.RevokedAt == null && x.ExpiresAt > now);
        }

        if (userId is not null)
        {
            query = query.Where(x => x.UserId == userId);
        }

        var rows = await (
                from s in query
                join revoker in _db.Users on s.RevokedBy equals revoker.Id into revokers
                from revoker in revokers.DefaultIfEmpty()
                orderby s.LastSeenAt descending
                select new
                {
                    s.Id,
                    User = new { s.User.Id, s.User.Nickname, s.User.Email, Role = s.User.Role.Code },
                    s.LoginMethod,
                    s.IpAddress,
                    s.UserAgent,
                    s.CreatedAt,
                    s.LastSeenAt,
                    s.ExpiresAt,
                    s.RevokedAt,
                    s.RevokeReason,
                    RevokedBy = revoker == null ? null : revoker.Nickname
                })
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();

        return rows.Select(s => new
        {
            s.Id,
            s.User,
            s.LoginMethod,
            Ip = s.IpAddress?.ToString(),
            s.UserAgent,
            s.CreatedAt,
            s.LastSeenAt,
            s.ExpiresAt,
            s.RevokedAt,
            s.RevokeReason,
            s.RevokedBy,
            Status = s.RevokedAt != null ? "REVOKED"
                : s.ExpiresAt <= now ? "EXPIRED"
                : s.LastSeenAt >= onlineSince ? "ONLINE"
                : "IDLE"
        });
    }

    public async Task<object> GetSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var since = now.AddHours(-24);
        var onlineSince = now - SessionService.OnlineWindow;

        var loginActions = new[] { AuditActions.Login, AuditActions.LoginFace, AuditActions.LoginQr };

        var logs24 = _db.AuditLogs.AsNoTracking().Where(x => x.CreatedAt >= since);

        return new
        {
            TotalUsers = await _db.Users.CountAsync(x => x.DeletedAt == null),
            ActiveUsers = await _db.Users.CountAsync(x => x.DeletedAt == null && x.IsActive),
            InactiveUsers = await _db.Users.CountAsync(x => x.DeletedAt == null && !x.IsActive),
            OnlineUsers = await _db.UserSessions
                .Where(x => x.RevokedAt == null && x.ExpiresAt > now && x.LastSeenAt >= onlineSince)
                .Select(x => x.UserId)
                .Distinct()
                .CountAsync(),
            OpenSessions = await _db.UserSessions.CountAsync(x => x.RevokedAt == null && x.ExpiresAt > now),
            Logins24h = await logs24.CountAsync(x => loginActions.Contains(x.Action)),
            FailedLogins24h = await logs24.CountAsync(x => x.Action == AuditActions.LoginFailed),
            Events24h = await logs24.CountAsync(),
            LoginsByMethod24h = await logs24
                .Where(x => loginActions.Contains(x.Action))
                .GroupBy(x => x.Action)
                .Select(g => new { Action = g.Key, Count = g.Count() })
                .ToListAsync()
        };
    }

    private static object? ParseDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(details);
        }
        catch (JsonException)
        {
            return details;
        }
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
