using System.Text.Json;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutomatasFinal.Infrastructure.Audit;

/// <summary>
/// Escribe en audit_logs. Reglas:
///   - NUNCA recibe ni guarda secretos: contraseñas, tokens de reset, JWT,
///     secretos de QR, descriptores faciales ni claves. "details" solo lleva
///     contexto seguro (método, motivo, campos cambiados, etc.).
///   - Usa su propio DbContext: un fallo al auditar no deja cambios a medias
///     en el contexto de la operación principal, y nunca la tumba.
/// </summary>
public class AuditService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <param name="action">Una constante de <see cref="AuditActions"/>.</param>
    /// <param name="success">Resultado de la acción.</param>
    /// <param name="targetUserId">Usuario afectado.</param>
    /// <param name="details">Contexto seguro (se guarda como jsonb).</param>
    /// <param name="actorUserId">Quién la hizo; si es null se toma del JWT de la petición.</param>
    public async Task LogAsync(
        string action,
        bool success,
        Guid? targetUserId = null,
        object? details = null,
        Guid? actorUserId = null)
    {
        try
        {
            var context = _httpContextAccessor.HttpContext;

            var log = new AuditLog
            {
                Action = action,
                Success = success,
                UserId = actorUserId ?? RequestInfo.GetUserId(context?.User),
                TargetUserId = targetUserId,
                Details = details is null
                    ? null
                    : JsonSerializer.Serialize(details, JsonOptions),
                IpAddress = RequestInfo.GetIp(context),
                UserAgent = RequestInfo.GetUserAgent(context),
                CreatedAt = DateTime.UtcNow
            };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.AuditLogs.Add(log);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No fue posible registrar la acción de auditoría {Action}.",
                action);
        }
    }
}
