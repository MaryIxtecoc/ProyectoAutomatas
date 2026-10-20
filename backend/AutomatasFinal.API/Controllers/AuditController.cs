using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Panel de auditoría: bitácora, usuarios conectados y cierre de sesiones.
/// Exclusivo de ADMIN.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public class AuditController : ControllerBase
{
    private readonly AuditQueryService _queries;
    private readonly SessionService _sessions;
    private readonly AuditService _audit;

    public AuditController(
        AuditQueryService queries,
        SessionService sessions,
        AuditService audit)
    {
        _queries = queries;
        _sessions = sessions;
        _audit = audit;
    }

    /// <summary>Acciones y categorías disponibles (para filtros y etiquetas).</summary>
    [HttpGet("catalog")]
    public IActionResult Catalog()
    {
        return Ok(new
        {
            categories = new[]
            {
                AuditActions.CategorySessions,
                AuditActions.CategoryUsers,
                AuditActions.CategoryProfile,
                AuditActions.CategorySecurity,
                AuditActions.CategoryAnalysis,
                AuditActions.CategorySystem
            },
            actions = AuditActions.All
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        return Ok(await _queries.GetSummaryAsync());
    }

    [HttpGet("logs")]
    public async Task<IActionResult> Logs(
        [FromQuery] string? category,
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] bool? success,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (search is { Length: > 100 })
        {
            return BadRequest(new { message = "La búsqueda no puede superar 100 caracteres." });
        }

        var result = await _queries.GetLogsAsync(new AuditQueryService.LogFilter(
            category, action, userId, success, from, to, search, page, pageSize));

        return Ok(result);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        return Ok(await _queries.GetUsersActivityAsync());
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(
        [FromQuery] bool onlyOpen = true,
        [FromQuery] Guid? userId = null,
        [FromQuery] int limit = 200)
    {
        return Ok(new
        {
            currentSessionId = SessionService.GetSessionId(User),
            items = await _queries.GetSessionsAsync(onlyOpen, userId, limit)
        });
    }

    /// <summary>Cierra una sesión concreta (p. ej. un equipo que quedó colgado).</summary>
    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeSession(Guid id)
    {
        var adminId = RequestInfo.GetUserId(User);

        var ownerId = await _sessions.RevokeAsync(id, adminId, SessionService.ReasonAdmin);

        if (ownerId is null)
        {
            return NotFound(new { message = "La sesión no existe o ya estaba cerrada." });
        }

        await _audit.LogAsync(
            AuditActions.SessionRevoked,
            true,
            ownerId,
            details: new { sessionId = id, scope = "UNA_SESION" });

        return Ok(new { message = "Sesión cerrada correctamente." });
    }

    /// <summary>Cierra todas las sesiones abiertas de un usuario.</summary>
    [HttpPost("users/{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeUserSessions(Guid id)
    {
        var adminId = RequestInfo.GetUserId(User);

        var closed = await _sessions.RevokeAllForUserAsync(id, adminId, SessionService.ReasonAdmin);

        await _audit.LogAsync(
            AuditActions.SessionRevoked,
            true,
            id,
            details: new { scope = "TODAS", sessionsClosed = closed });

        return Ok(new
        {
            message = closed == 0
                ? "El usuario no tenía sesiones abiertas."
                : $"Se cerraron {closed} sesión(es) del usuario.",
            closed
        });
    }
}
