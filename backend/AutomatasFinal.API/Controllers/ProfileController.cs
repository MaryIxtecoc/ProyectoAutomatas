using System.Security.Claims;
using AutomatasFinal.Application.Users;
using AutomatasFinal.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly ProfileService _profileService;

    public ProfileController(ProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        var user = await _profileService.GetMyProfileAsync(userId.Value);

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
            RoleCode = user.Role.Code,
            RoleName = user.Role.Name,
            NotificationMethod = user.NotificationMethod.ToString(),
            user.PhotoOriginalPath,
            user.PhotoModifiedPath,
            user.CreatedAt,
            CredentialIssuedAt = await _profileService.GetMyActiveCredentialIssuedAtAsync(user.Id)
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateMyProfile(
        [FromBody] UpdateProfileRequest request)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        try
        {
            var updated = await _profileService.UpdateMyProfileAsync(
                userId.Value,
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
                message = "Perfil actualizado correctamente."
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

    [HttpPatch("password")]
    public async Task<IActionResult> ChangeMyPassword(
        [FromBody] ChangePasswordRequest request)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        try
        {
            var changed = await _profileService.ChangeMyPasswordAsync(
                userId.Value,
                request);

            if (!changed)
            {
                return NotFound(new
                {
                    message = "Usuario no encontrado."
                });
            }

            return Ok(new
            {
                message = "Contraseña actualizada correctamente."
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

    /// <summary>
    /// Cambia la foto: "original" (sin editar, debe tener un rostro) y
    /// "modified" (editada, opcional). Se acepta "file" como original por
    /// compatibilidad.
    /// </summary>
    [HttpPost("photo")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UpdateMyPhoto(
        IFormFile? original,
        IFormFile? modified,
        IFormFile? file)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        original ??= file;

        var error = ValidateImage(original, required: true) ?? ValidateImage(modified, required: false);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        try
        {
            var path = await _profileService.UpdateMyPhotoAsync(
                userId.Value,
                await ReadAllAsync(original!),
                modified is null ? null : await ReadAllAsync(modified));

            if (path is null)
            {
                return NotFound(new
                {
                    message = "Usuario no encontrado."
                });
            }

            return Ok(new
            {
                message = "Fotografía actualizada correctamente."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "No fue posible actualizar la fotografía."
                });
        }
    }

    private static string? ValidateImage(IFormFile? image, bool required)
    {
        if (image is null || image.Length == 0)
        {
            return required ? "La fotografía es obligatoria." : null;
        }

        if (image.Length > 5 * 1024 * 1024)
        {
            return "Cada imagen puede pesar como máximo 5 MB.";
        }

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };

        return allowedTypes.Contains(image.ContentType)
            ? null
            : "Solo se permiten imágenes JPG, PNG o WEBP.";
    }

    private static async Task<byte[]> ReadAllAsync(IFormFile image)
    {
        await using var stream = image.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    [HttpGet("photo/original")]
    public Task<IActionResult> GetMyOriginalPhoto() => GetMyPhotoAsync(modified: false);

    [HttpGet("photo/modified")]
    public Task<IActionResult> GetMyModifiedPhoto() => GetMyPhotoAsync(modified: true);

    private async Task<IActionResult> GetMyPhotoAsync(bool modified)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        var photo = await _profileService.GetMyPhotoAsync(userId.Value, modified);

        if (photo is null)
        {
            return NotFound(new
            {
                message = "Todavía no tienes una fotografía guardada."
            });
        }

        return File(photo.Value.Bytes, photo.Value.ContentType);
    }

    /// <summary>
    /// Descarga el PDF de la credencial activa (pasa por el backend: el bucket
    /// es privado y no se entrega ninguna URL de Storage). No genera nada.
    /// </summary>
    [HttpGet("credential")]
    public async Task<IActionResult> GetMyCredential()
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        try
        {
            var pdfBytes =
                await _profileService.GetMyActiveCredentialPdfAsync(
                    userId.Value);

            if (pdfBytes is null)
            {
                return NotFound(new
                {
                    message = "Todavía no tienes una credencial. Genérala desde tu perfil."
                });
            }

            return File(
                pdfBytes,
                "application/pdf",
                "credencial.pdf");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "No fue posible descargar la credencial."
                });
        }
    }

    /// <summary>
    /// Genera o regenera la credencial. La anterior queda inactiva (su QR deja
    /// de servir) y la nueva se envía por el método de notificación del usuario.
    /// </summary>
    [HttpPost("credential/regenerate")]
    public async Task<IActionResult> RegenerateCredential()
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        string? error;

        try
        {
            error = await _profileService.RegenerateCredentialAsync(userId.Value);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            error = "No fue posible generar la credencial. Intenta de nuevo en unos minutos.";
        }

        if (error is not null)
        {
            return BadRequest(new
            {
                message = error
            });
        }

        return Ok(new
        {
            message = "Credencial generada. También te la enviamos por tu método de notificación."
        });
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (claim is null)
        {
            return null;
        }

        return Guid.TryParse(claim.Value, out var userId)
            ? userId
            : null;
    }
}
