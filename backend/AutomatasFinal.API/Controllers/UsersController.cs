using System.Security.Claims;
using System.Text.Json;
using AutomatasFinal.Application.Users;
using AutomatasFinal.Infrastructure.Data;
using AutomatasFinal.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.API.Controllers;

// ADMIN y SUPERVISOR pueden ver la lista (GetAll/GetById); crear, editar,
// cambiar rol, activar/desactivar y eliminar quedan restringidos a ADMIN
// con [Authorize(Roles = "ADMIN")] en cada acción (las dos reglas se
// combinan: para esas acciones hace falta cumplir ambas, o sea ser ADMIN).
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN,SUPERVISOR")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly AppDbContext _db;

    public UsersController(UserService userService, AppDbContext db)
    {
        _userService = userService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllAsync();

        var analysisCounts = await _db.AnalysisJobs
            .GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        return Ok(users.Select(x => new
        {
            x.Id,
            x.Email,
            x.Phone,
            x.BirthDate,
            x.Nickname,
            x.RoleId,
            Role = x.Role.Code,
            RoleName = x.Role.Name,
            NotificationMethod = x.NotificationMethod.ToString(),
            x.IsActive,
            x.CreatedAt,
            x.UpdatedAt,
            x.LastLoginAt,
            AnalysisCount = analysisCounts.GetValueOrDefault(x.Id, 0)
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userService.GetByIdAsync(id);

        if (user is null)
        {
            return NotFound(new
            {
                message = "Usuario no encontrado."
            });
        }

        return Ok(new
        {
            user.Id,
            user.Email,
            user.Phone,
            user.BirthDate,
            user.Nickname,
            user.RoleId,
            Role = user.Role.Code,
            RoleName = user.Role.Name,
            NotificationMethod = user.NotificationMethod.ToString(),
            user.IsActive,
            user.CreatedAt,
            user.UpdatedAt
        });
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request)
    {
        try
        {
            var user = await _userService.CreateAsync(request);

            return Ok(new
            {
                message = "Usuario creado correctamente.",
                userId = user.Id
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest request)
    {
        try
        {
            var updated = await _userService.UpdateAsync(
                id,
                request);

            if (!updated)
            {
                return NotFound(new
                {
                    message = "Usuario no encontrado."
                });
            }

            return Ok(new
            {
                message = "Usuario actualizado correctamente."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPatch("{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(
        Guid id,
        [FromBody] short roleId)
    {
        try
        {
            var changed = await _userService.ChangeRoleAsync(
                id,
                roleId);

            if (!changed)
            {
                return NotFound(new
                {
                    message = "Usuario no encontrado."
                });
            }

            return Ok(new
            {
                message = "Rol actualizado correctamente."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetActive(
        Guid id,
        [FromBody] JsonElement body)
    {
        // Acepta los dos formatos que se usan en el proyecto:
        //   - booleano solo:  true / false            (colección de Postman)
        //   - objeto:         { "isActive": false }   (frontend, users.vue)
        // Antes solo aceptaba el booleano, por eso desde la página fallaba con
        // "The JSON value could not be converted to System.Boolean".
        if (!TryReadIsActive(body, out var isActive))
        {
            return BadRequest(new
            {
                message = "Datos inválidos: se esperaba el estado del usuario (activo o inactivo)."
            });
        }

        // Un ADMIN no puede desactivar su propia cuenta: perdería el acceso
        // y, si es el único ADMIN, nadie podría volver a activarla.
        if (!isActive && IsCurrentUser(id))
        {
            return BadRequest(new
            {
                message = "No puedes desactivar tu propia cuenta. Pide a otro administrador que lo haga."
            });
        }

        var changed = await _userService.SetActiveAsync(
            id,
            isActive);

        if (!changed)
        {
            return NotFound(new
            {
                message = "Usuario no encontrado."
            });
        }

        return Ok(new
        {
            message = isActive
                ? "Usuario activado correctamente."
                : "Usuario desactivado correctamente."
        });
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _userService.DeleteAsync(id);

        if (!deleted)
        {
            return NotFound(new
            {
                message = "Usuario no encontrado."
            });
        }

        return Ok(new
        {
            message = "Usuario eliminado correctamente."
        });
    }

    private bool IsCurrentUser(Guid id)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(claim, out var currentUserId) &&
               currentUserId == id;
    }

    private static bool TryReadIsActive(JsonElement body, out bool isActive)
    {
        isActive = false;

        switch (body.ValueKind)
        {
            case JsonValueKind.True:
                isActive = true;
                return true;

            case JsonValueKind.False:
                return true;

            case JsonValueKind.Object:
                foreach (var property in body.EnumerateObject())
                {
                    if (string.Equals(property.Name, "isActive", StringComparison.OrdinalIgnoreCase) &&
                        (property.Value.ValueKind == JsonValueKind.True ||
                         property.Value.ValueKind == JsonValueKind.False))
                    {
                        isActive = property.Value.GetBoolean();
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
