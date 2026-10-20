using AutomatasFinal.Application.Users;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Solicitudes de corrección de email / fecha de nacimiento. Cualquier usuario
/// autenticado crea y ve las suyas; listar, aprobar y rechazar es solo ADMIN.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileChangeRequestsController : ControllerBase
{
    private readonly ProfileChangeRequestService _service;

    public ProfileChangeRequestsController(ProfileChangeRequestService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProfileChangeRequest request)
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        try
        {
            var created = await _service.CreateAsync(userId.Value, request);

            return Ok(new
            {
                message = "Solicitud enviada. Un administrador la revisará.",
                id = created.Id,
                status = created.Status
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        return Ok(await _service.GetMineAsync(userId.Value));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        return Ok(await _service.ListAsync(status));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, [FromBody] ReviewProfileChangeRequest? body) =>
        Review(() => _service.ApproveAsync(id, RequestInfo.GetUserId(User)!.Value, body?.Comment),
            "Solicitud aprobada. Los datos del usuario fueron actualizados.");

    [Authorize(Roles = "ADMIN")]
    [HttpPost("{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id, [FromBody] ReviewProfileChangeRequest? body) =>
        Review(() => _service.RejectAsync(id, RequestInfo.GetUserId(User)!.Value, body?.Comment),
            "Solicitud rechazada.");

    private async Task<IActionResult> Review(Func<Task> action, string okMessage)
    {
        if (RequestInfo.GetUserId(User) is null)
        {
            return Unauthorized(new { message = "Usuario no válido." });
        }

        try
        {
            await action();
            return Ok(new { message = okMessage });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
