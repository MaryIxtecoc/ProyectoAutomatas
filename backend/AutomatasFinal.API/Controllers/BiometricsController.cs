using System.Security.Claims;
using AutomatasFinal.Application.Biometrics;
using AutomatasFinal.Infrastructure.Biometrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BiometricsController : ControllerBase
{
    private readonly BiometricService _biometricService;

    public BiometricsController(
        BiometricService biometricService)
    {
        _biometricService = biometricService;
    }

    [HttpPost("face")]
    public async Task<IActionResult> SaveFace(
        IFormFile image)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        if (image is null || image.Length == 0)
        {
            return BadRequest(new
            {
                message = "La imagen facial es obligatoria."
            });
        }

        if (image.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new
            {
                message = "La imagen no puede superar los 5 MB."
            });
        }

        try
        {
            await using var stream = image.OpenReadStream();
            using var memoryStream = new MemoryStream();

            await stream.CopyToAsync(memoryStream);

            await _biometricService.SaveFaceImageAsync(
                userId.Value,
                memoryStream.ToArray());

            return Ok(new
            {
                message =
                    "Datos biométricos faciales registrados correctamente."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    // El detalle queda solo en la consola del servidor.
                    message = "Ocurrió un error al procesar la imagen facial."
                });
        }
    }

    [HttpPost("face/verify")]
    public async Task<IActionResult> VerifyFace(
        IFormFile image)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        if (image is null || image.Length == 0)
        {
            return BadRequest(new
            {
                message = "La imagen facial es obligatoria."
            });
        }

        if (image.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new
            {
                message = "La imagen no puede superar los 5 MB."
            });
        }

        try
        {
            await using var stream = image.OpenReadStream();
            using var memoryStream = new MemoryStream();

            await stream.CopyToAsync(memoryStream);

            var result =
                await _biometricService.VerifyFaceAsync(
                    userId.Value,
                    memoryStream.ToArray());

            return Ok(result);
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
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    // El detalle queda solo en la consola del servidor.
                    message = "Ocurrió un error al procesar la imagen facial."
                });
        }
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyBiometric()
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        var biometric =
            await _biometricService.GetMyBiometricAsync(
                userId.Value);

        if (biometric is null)
        {
            return Ok(new
            {
                registered = false
            });
        }

        return Ok(biometric);
    }

    [HttpDelete("face")]
    public async Task<IActionResult> DeleteFace()
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "Usuario no válido."
            });
        }

        var deleted =
            await _biometricService.DeleteFaceDescriptorAsync(
                userId.Value);

        if (!deleted)
        {
            return NotFound(new
            {
                message =
                    "El usuario no tiene datos biométricos registrados."
            });
        }

        return Ok(new
        {
            message =
                "Datos biométricos eliminados correctamente."
        });
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(
            ClaimTypes.NameIdentifier);

        if (claim is null)
        {
            return null;
        }

        return Guid.TryParse(
            claim.Value,
            out var userId)
            ? userId
            : null;
    }
}