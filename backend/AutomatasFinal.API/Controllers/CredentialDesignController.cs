using AutomatasFinal.Application.Credentials;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Credentials;
using AutomatasFinal.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Sandbox de diseño de la credencial. Solo apariencia: el usuario sale del
/// JWT y la identidad impresa sale de la BD. Para emitir la credencial con el
/// diseño guardado se usa POST /api/Profile/credential/regenerate.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CredentialDesignController : ControllerBase
{
    private readonly CredentialDesignService _designs;
    private readonly ProfileService _profile;

    public CredentialDesignController(
        CredentialDesignService designs,
        ProfileService profile)
    {
        _designs = designs;
        _profile = profile;
    }

    /// <summary>Diseño actual (o el por defecto) y opciones permitidas.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        return Ok(new
        {
            design = await _designs.GetForUserAsync(userId.Value),
            defaults = new CredentialDesignDto(),
            options = new
            {
                templates = CredentialDesignService.Templates,
                photoStyles = CredentialDesignService.PhotoStyles,
                qrStyles = CredentialDesignService.QrStyles,
                maxDecorations = CredentialDesignService.MaxDecorations
            }
        });
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] CredentialDesignDto design)
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        try
        {
            var saved = await _designs.SaveAsync(userId.Value, design);
            return Ok(new { message = "Diseño guardado.", design = saved });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Restaura el diseño por defecto.</summary>
    [HttpDelete]
    public async Task<IActionResult> Reset()
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        await _designs.ResetAsync(userId.Value);

        return Ok(new { message = "Diseño restaurado.", design = new CredentialDesignDto() });
    }

    /// <summary>PDF de vista previa con el diseño enviado (sin guardarlo). QR sin validez.</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] CredentialDesignDto design)
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        CredentialDesignDto validated;

        try
        {
            validated = CredentialDesignService.Validate(design);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        try
        {
            var pdf = await _profile.RenderCredentialPreviewAsync(userId.Value, validated);

            if (pdf is null)
            {
                return BadRequest(new { message = "Necesitas una fotografía de perfil para ver la vista previa." });
            }

            return File(pdf, "application/pdf", "vista-previa-credencial.pdf");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "No fue posible generar la vista previa." });
        }
    }

    // --- Stickers ---

    [HttpGet("stickers")]
    public async Task<IActionResult> Stickers()
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        return Ok(await _designs.ListStickersAsync(userId.Value));
    }

    [HttpPost("stickers")]
    [RequestSizeLimit(CredentialDesignService.MaxStickerBytes + 64 * 1024)]
    public async Task<IActionResult> UploadSticker(IFormFile file)
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        try
        {
            var name = await _designs.UploadUserStickerAsync(userId.Value, file);
            return Ok(new { message = "Sticker subido.", file = name });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "No fue posible subir el sticker." });
        }
    }

    /// <summary>Imagen de un sticker propio (privado: solo su dueño).</summary>
    [HttpGet("stickers/mine/{file}")]
    public async Task<IActionResult> MySticker(string file)
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        var bytes = await _designs.GetUserStickerAsync(userId.Value, file);

        return bytes is null ? NotFound() : File(bytes, "image/png");
    }

    [HttpDelete("stickers/mine/{file}")]
    public async Task<IActionResult> DeleteSticker(string file)
    {
        var userId = RequestInfo.GetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Usuario no válido." });

        var deleted = await _designs.DeleteUserStickerAsync(userId.Value, file);

        return deleted
            ? Ok(new { message = "Sticker eliminado." })
            : NotFound(new { message = "Sticker no encontrado." });
    }
}
