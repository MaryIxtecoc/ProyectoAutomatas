using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AutomatasFinal.Infrastructure.Sessions;

/// <summary>
/// Sesiones revocables sobre JWT. Cada login crea una fila en user_sessions y
/// su Id viaja en el JWT (claim <see cref="SessionClaim"/>). En cada petición
/// autenticada se comprueba que la sesión siga abierta.
///
/// Para no consultar la BD en cada request, el resultado se guarda en memoria
/// <see cref="ValidationCacheDuration"/>. Al revocar desde este backend se
/// borra la entrada de caché, así que el cierre es inmediato.
/// </summary>
public class SessionService
{
    public const string SessionClaim = "session_id";

    public const string MethodPassword = "PASSWORD";
    public const string MethodFace = "FACE";
    public const string MethodQr = "QR";
    public const string MethodRegister = "REGISTER";

    public const string ReasonLogout = "LOGOUT";
    public const string ReasonAdmin = "ADMIN";
    public const string ReasonUserDisabled = "USER_DISABLED";
    public const string ReasonUserDeleted = "USER_DELETED";
    public const string ReasonRoleChanged = "ROLE_CHANGED";
    public const string ReasonPasswordChanged = "PASSWORD_CHANGED";
    public const string ReasonPasswordReset = "PASSWORD_RESET";

    /// <summary>Una sesión con actividad dentro de esta ventana se considera "en línea".</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ValidationCacheDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LastSeenUpdateInterval = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionService(
        AppDbContext db,
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<UserSession> CreateAsync(
        Guid userId,
        string loginMethod,
        DateTime expiresAt)
    {
        var context = _httpContextAccessor.HttpContext;
        var now = DateTime.UtcNow;

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LoginMethod = loginMethod,
            IpAddress = RequestInfo.GetIp(context),
            UserAgent = RequestInfo.GetUserAgent(context),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = expiresAt
        };

        _db.UserSessions.Add(session);

        await _db.SaveChangesAsync();

        return session;
    }

    /// <summary>
    /// true si la sesión existe, pertenece al usuario, no fue revocada, no
    /// expiró y el usuario sigue activo. Actualiza last_seen_at (como mucho
    /// una vez por minuto por sesión).
    /// </summary>
    public async Task<bool> IsActiveAsync(
        Guid sessionId,
        Guid userId)
    {
        var cacheKey = ValidationKey(sessionId);

        if (_cache.TryGetValue(cacheKey, out bool cached))
        {
            return cached;
        }

        var now = DateTime.UtcNow;

        var active = await _db.UserSessions
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == sessionId &&
                x.UserId == userId &&
                x.RevokedAt == null &&
                x.ExpiresAt > now &&
                x.User.IsActive &&
                x.User.DeletedAt == null);

        _cache.Set(cacheKey, active, ValidationCacheDuration);

        if (active &&
            !_cache.TryGetValue(LastSeenKey(sessionId), out _))
        {
            _cache.Set(LastSeenKey(sessionId), true, LastSeenUpdateInterval);

            await _db.UserSessions
                .Where(x => x.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now));
        }

        return active;
    }

    /// <summary>Revoca una sesión. Devuelve el dueño, o null si no existía o ya estaba cerrada.</summary>
    public async Task<Guid?> RevokeAsync(
        Guid sessionId,
        Guid? revokedBy,
        string reason)
    {
        var userId = await _db.UserSessions
            .Where(x => x.Id == sessionId && x.RevokedAt == null)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync();

        if (userId is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;

        await _db.UserSessions
            .Where(x => x.Id == sessionId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedBy, revokedBy)
                .SetProperty(x => x.RevokeReason, reason));

        _cache.Remove(ValidationKey(sessionId));

        return userId;
    }

    /// <summary>Revoca todas las sesiones abiertas del usuario (menos <paramref name="exceptSessionId"/>).</summary>
    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        Guid? revokedBy,
        string reason,
        Guid? exceptSessionId = null)
    {
        var sessionIds = await _db.UserSessions
            .Where(x =>
                x.UserId == userId &&
                x.RevokedAt == null &&
                x.Id != exceptSessionId)
            .Select(x => x.Id)
            .ToListAsync();

        if (sessionIds.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;

        await _db.UserSessions
            .Where(x => sessionIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedBy, revokedBy)
                .SetProperty(x => x.RevokeReason, reason));

        foreach (var id in sessionIds)
        {
            _cache.Remove(ValidationKey(id));
        }

        return sessionIds.Count;
    }

    public static Guid? GetSessionId(System.Security.Claims.ClaimsPrincipal? user)
    {
        var claim = user?.FindFirst(SessionClaim);

        return Guid.TryParse(claim?.Value, out var sessionId)
            ? sessionId
            : null;
    }

    private static string ValidationKey(Guid sessionId) => $"session:valid:{sessionId}";
    private static string LastSeenKey(Guid sessionId) => $"session:seen:{sessionId}";
}
