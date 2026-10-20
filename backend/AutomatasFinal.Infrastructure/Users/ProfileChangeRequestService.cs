using System.Net.Mail;
using AutomatasFinal.Application.Users;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Users;

/// <summary>
/// Solicitudes de corrección de email / fecha de nacimiento. El usuario las
/// crea; solo ADMIN las aprueba o rechaza (autorización en el controlador).
/// </summary>
public class ProfileChangeRequestService
{
    private const int MinReasonLength = 10;
    private const int MaxReasonLength = 500;
    private const int MaxEmailLength = 254;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public ProfileChangeRequestService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ProfileChangeRequest> CreateAsync(Guid userId, CreateProfileChangeRequest request)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive && x.DeletedAt == null)
            ?? throw new ArgumentException("Usuario no válido.");

        var reason = request.Reason?.Trim() ?? string.Empty;

        if (reason.Length < MinReasonLength || reason.Length > MaxReasonLength)
        {
            throw new ArgumentException(
                $"El motivo debe tener entre {MinReasonLength} y {MaxReasonLength} caracteres.");
        }

        string? email = null;

        if (!string.IsNullOrWhiteSpace(request.RequestedEmail))
        {
            email = NormalizeEmail(request.RequestedEmail);

            if (string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                email = null;
            }
            else if (await EmailInUseAsync(email, userId))
            {
                throw new InvalidOperationException("Ese correo ya está registrado en otra cuenta.");
            }
        }

        DateOnly? birthDate = null;

        if (request.RequestedBirthDate is not null)
        {
            ValidateBirthDate(request.RequestedBirthDate.Value);

            if (request.RequestedBirthDate.Value != user.BirthDate)
            {
                birthDate = request.RequestedBirthDate.Value;
            }
        }

        if (email is null && birthDate is null)
        {
            throw new ArgumentException(
                "Indica un correo o una fecha de nacimiento distintos a los actuales.");
        }

        var hasPending = await _db.ProfileChangeRequests
            .AnyAsync(x => x.UserId == userId && x.Status == ProfileChangeRequest.StatusPending);

        if (hasPending)
        {
            throw new InvalidOperationException(
                "Ya tienes una solicitud pendiente. Espera a que sea revisada.");
        }

        var entity = new ProfileChangeRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RequestedEmail = email,
            RequestedBirthDate = birthDate,
            Reason = reason,
            Status = ProfileChangeRequest.StatusPending,
            CreatedAt = DateTime.UtcNow
        };

        _db.ProfileChangeRequests.Add(entity);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            AuditActions.ProfileChangeRequested,
            true,
            userId,
            details: new
            {
                requestId = entity.Id,
                changes = Changes(user.Email, email, user.BirthDate, birthDate)
            });

        return entity;
    }

    public async Task<object> GetMineAsync(Guid userId)
    {
        return await _db.ProfileChangeRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.RequestedEmail,
                x.RequestedBirthDate,
                x.Reason,
                x.Status,
                x.CreatedAt,
                x.ReviewedAt
            })
            .ToListAsync();
    }

    public async Task<object> ListAsync(string? status)
    {
        var query = _db.ProfileChangeRequests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToUpperInvariant();
            query = query.Where(x => x.Status == normalized);
        }

        return await query
            .OrderBy(x => x.Status == ProfileChangeRequest.StatusPending ? 0 : 1)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.Reason,
                x.CreatedAt,
                x.ReviewedAt,
                User = new
                {
                    x.User.Id,
                    x.User.Nickname,
                    CurrentEmail = x.User.Email,
                    CurrentBirthDate = x.User.BirthDate,
                    x.User.IsActive,
                    Deleted = x.User.DeletedAt != null
                },
                x.RequestedEmail,
                x.RequestedBirthDate,
                Reviewer = x.Reviewer == null ? null : x.Reviewer.Nickname
            })
            .Take(500)
            .ToListAsync();
    }

    /// <summary>Aprueba y aplica SOLO email y/o fecha de nacimiento.</summary>
    public async Task ApproveAsync(Guid requestId, Guid reviewerId, string? comment)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var request = await _db.ProfileChangeRequests
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == requestId)
            ?? throw new KeyNotFoundException("Solicitud no encontrada.");

        EnsureReviewable(request, reviewerId);

        var user = request.User;

        if (!user.IsActive || user.DeletedAt != null)
        {
            throw new InvalidOperationException("La cuenta del usuario está inactiva o eliminada.");
        }

        if (request.RequestedEmail is not null &&
            await EmailInUseAsync(request.RequestedEmail, user.Id))
        {
            throw new InvalidOperationException(
                "El correo solicitado ya está en uso por otra cuenta. Rechaza la solicitud.");
        }

        // Marca la solicitud solo si sigue PENDING (dos revisores a la vez: gana uno).
        var now = DateTime.UtcNow;

        var updated = await _db.ProfileChangeRequests
            .Where(x => x.Id == requestId && x.Status == ProfileChangeRequest.StatusPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ProfileChangeRequest.StatusApproved)
                .SetProperty(x => x.ReviewedBy, reviewerId)
                .SetProperty(x => x.ReviewedAt, now));

        if (updated == 0)
        {
            throw new InvalidOperationException("La solicitud ya fue revisada por otra persona.");
        }

        var changes = Changes(user.Email, request.RequestedEmail, user.BirthDate, request.RequestedBirthDate);

        if (request.RequestedEmail is not null)
        {
            user.Email = request.RequestedEmail;
        }

        if (request.RequestedBirthDate is not null)
        {
            user.BirthDate = request.RequestedBirthDate.Value;
        }

        user.UpdatedAt = now;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        await _audit.LogAsync(
            AuditActions.ProfileChangeApproved,
            true,
            user.Id,
            details: new { requestId, changes, comment = Trim(comment) });
    }

    public async Task RejectAsync(Guid requestId, Guid reviewerId, string? comment)
    {
        var request = await _db.ProfileChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requestId)
            ?? throw new KeyNotFoundException("Solicitud no encontrada.");

        EnsureReviewable(request, reviewerId);

        var updated = await _db.ProfileChangeRequests
            .Where(x => x.Id == requestId && x.Status == ProfileChangeRequest.StatusPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ProfileChangeRequest.StatusRejected)
                .SetProperty(x => x.ReviewedBy, reviewerId)
                .SetProperty(x => x.ReviewedAt, DateTime.UtcNow));

        if (updated == 0)
        {
            throw new InvalidOperationException("La solicitud ya fue revisada por otra persona.");
        }

        await _audit.LogAsync(
            AuditActions.ProfileChangeRejected,
            true,
            request.UserId,
            details: new { requestId, comment = Trim(comment) });
    }

    private static void EnsureReviewable(ProfileChangeRequest request, Guid reviewerId)
    {
        if (request.Status != ProfileChangeRequest.StatusPending)
        {
            throw new InvalidOperationException("La solicitud ya fue revisada.");
        }

        // Separación de funciones: nadie aprueba ni rechaza su propia solicitud.
        if (request.UserId == reviewerId)
        {
            throw new InvalidOperationException("No puedes revisar tu propia solicitud.");
        }
    }

    private Task<bool> EmailInUseAsync(string email, Guid exceptUserId) =>
        _db.Users.AnyAsync(x => x.Email == email && x.Id != exceptUserId);

    private static string NormalizeEmail(string value)
    {
        var email = value.Trim().ToLowerInvariant();

        if (email.Length > MaxEmailLength ||
            !MailAddress.TryCreate(email, out var parsed) ||
            parsed.Address != email ||
            !email.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("El correo electrónico no es válido.");
        }

        return email;
    }

    private static void ValidateBirthDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (date > today)
        {
            throw new ArgumentException("La fecha de nacimiento no puede ser futura.");
        }

        if (date > today.AddYears(-5) || date < today.AddYears(-120))
        {
            throw new ArgumentException("La fecha de nacimiento no es válida.");
        }
    }

    private static List<object> Changes(
        string currentEmail,
        string? requestedEmail,
        DateOnly currentBirthDate,
        DateOnly? requestedBirthDate)
    {
        var changes = new List<object>();

        if (requestedEmail is not null)
        {
            changes.Add(new { field = "email", from = currentEmail, to = requestedEmail });
        }

        if (requestedBirthDate is not null)
        {
            changes.Add(new
            {
                field = "birthDate",
                from = currentBirthDate.ToString("yyyy-MM-dd"),
                to = requestedBirthDate.Value.ToString("yyyy-MM-dd")
            });
        }

        return changes;
    }

    private static string? Trim(string? comment)
    {
        var value = comment?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length > 500 ? value[..500] : value;
    }
}
