using AutomatasFinal.Application.Security;
using AutomatasFinal.Application.Users;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Data;
using AutomatasFinal.Infrastructure.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Users;

public class UserService
{
    private readonly AppDbContext _db;
    private readonly PasswordService _passwordService;
    private readonly SessionService _sessionService;
    private readonly AuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserService(
        AppDbContext db,
        PasswordService passwordService,
        SessionService sessionService,
        AuditService audit,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _passwordService = passwordService;
        _sessionService = sessionService;
        _audit = audit;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await _db.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _db.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.DeletedAt == null);
    }

    public async Task<User> CreateAsync(
        CreateUserRequest request)
    {
        var email = request.Email
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException(
                "El correo electrónico es obligatorio.");

        PasswordPolicy.Validate(request.Password);

        var phone = PhonePolicy.Normalize(request.Phone);

        if (string.IsNullOrWhiteSpace(request.Nickname))
            throw new ArgumentException(
                "El nombre de usuario es obligatorio.");

        var exists = await _db.Users
            .AnyAsync(x => x.Email == email);

        if (exists)
        {
            throw new InvalidOperationException(
                "El correo electrónico ya está registrado.");
        }

        var roleExists = await _db.Roles
            .AnyAsync(x => x.Id == request.RoleId);

        if (!roleExists)
        {
            throw new ArgumentException(
                "El rol seleccionado no existe.");
        }

        if (!Enum.TryParse<NotificationMethod>(
                request.NotificationMethod.Trim(),
                true,
                out var notificationMethod))
        {
            throw new ArgumentException(
                "Método de notificación inválido. Valores permitidos: EMAIL, WHATSAPP, BOTH.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Phone = phone,
            BirthDate = request.BirthDate,
            Nickname = request.Nickname.Trim(),
            PasswordHash = _passwordService.HashPassword(
                request.Password),
            RoleId = request.RoleId,
            NotificationMethod = notificationMethod,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);

        await _db.SaveChangesAsync();

        var roleCode = await GetRoleCodeAsync(user.RoleId);

        await _audit.LogAsync(
            AuditActions.UserCreated,
            true,
            user.Id,
            details: new
            {
                email = user.Email,
                nickname = user.Nickname,
                role = roleCode,
                notificationMethod = user.NotificationMethod.ToString()
            });

        return user;
    }

    public async Task<bool> UpdateAsync(
        Guid id,
        UpdateUserRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.DeletedAt == null);

        if (user is null)
            return false;

        var email = request.Email
            .Trim()
            .ToLowerInvariant();

        var updatedPhone = PhonePolicy.Normalize(request.Phone);

        var emailExists = await _db.Users
            .AnyAsync(x =>
                x.Email == email &&
                x.Id != id);

        if (emailExists)
        {
            throw new InvalidOperationException(
                "El correo electrónico ya está registrado.");
        }

        if (!Enum.TryParse<NotificationMethod>(
                request.NotificationMethod.Trim(),
                true,
                out var notificationMethod))
        {
            throw new ArgumentException(
                "Método de notificación inválido. Valores permitidos: EMAIL, WHATSAPP, BOTH.");
        }

        var changes = new List<object>();

        AddChange(changes, "email", user.Email, email);
        AddChange(changes, "phone", user.Phone, updatedPhone);
        AddChange(changes, "birthDate", user.BirthDate.ToString("yyyy-MM-dd"), request.BirthDate.ToString("yyyy-MM-dd"));
        AddChange(changes, "nickname", user.Nickname, request.Nickname.Trim());
        AddChange(changes, "notificationMethod", user.NotificationMethod.ToString(), notificationMethod.ToString());

        user.Email = email;
        user.Phone = updatedPhone;
        user.BirthDate = request.BirthDate;
        user.Nickname = request.Nickname.Trim();
        user.NotificationMethod = notificationMethod;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            AuditActions.UserUpdated,
            true,
            user.Id,
            details: new { changes });

        return true;
    }

    public async Task<bool> ChangeRoleAsync(
        Guid id,
        short roleId)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.DeletedAt == null);

        if (user is null)
            return false;

        var roleExists = await _db.Roles
            .AnyAsync(x => x.Id == roleId);

        if (!roleExists)
        {
            throw new ArgumentException(
                "El rol seleccionado no existe.");
        }

        var previousRoleId = user.RoleId;

        user.RoleId = roleId;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        if (previousRoleId == roleId)
        {
            return true;
        }

        // El JWT lleva el rol: se cierran sus sesiones para que entre con el nuevo.
        var closed = await _sessionService.RevokeAllForUserAsync(
            user.Id,
            RequestActorId(),
            SessionService.ReasonRoleChanged);

        await _audit.LogAsync(
            AuditActions.RoleChanged,
            true,
            user.Id,
            details: new
            {
                from = await GetRoleCodeAsync(previousRoleId),
                to = await GetRoleCodeAsync(roleId),
                sessionsClosed = closed
            });

        return true;
    }

    public async Task<bool> SetActiveAsync(
        Guid id,
        bool isActive)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.DeletedAt == null);

        if (user is null)
            return false;

        var wasActive = user.IsActive;

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        if (wasActive == isActive)
        {
            return true;
        }

        var closed = isActive
            ? 0
            : await _sessionService.RevokeAllForUserAsync(
                user.Id,
                RequestActorId(),
                SessionService.ReasonUserDisabled);

        await _audit.LogAsync(
            isActive ? AuditActions.UserEnabled : AuditActions.UserDisabled,
            true,
            user.Id,
            details: new { sessionsClosed = closed });

        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.DeletedAt == null);

        if (user is null)
            return false;

        user.IsActive = false;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var closed = await _sessionService.RevokeAllForUserAsync(
            user.Id,
            RequestActorId(),
            SessionService.ReasonUserDeleted);

        await _audit.LogAsync(
            AuditActions.UserDeleted,
            true,
            user.Id,
            details: new { email = user.Email, nickname = user.Nickname, sessionsClosed = closed });

        return true;
    }

    /// <summary>El ADMIN que hace la acción (del JWT de la petición).</summary>
    private Guid? RequestActorId() =>
        RequestInfo.GetUserId(_httpContextAccessor.HttpContext?.User);

    private async Task<string?> GetRoleCodeAsync(short roleId) =>
        await _db.Roles
            .Where(x => x.Id == roleId)
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

    private static void AddChange(
        List<object> changes,
        string field,
        string? from,
        string? to)
    {
        if (!string.Equals(from, to, StringComparison.Ordinal))
        {
            changes.Add(new { field, from, to });
        }
    }
}