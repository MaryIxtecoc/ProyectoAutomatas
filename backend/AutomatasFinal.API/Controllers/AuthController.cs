using AutomatasFinal.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AutomatasFinal.Infrastructure.Auth;
using AutomatasFinal.Infrastructure.Security;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Sessions;


namespace AutomatasFinal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IRecaptchaService _recaptchaService;

    public AuthController(AuthService authService, IRecaptchaService recaptchaService)
    {
        _authService = authService;
        _recaptchaService = recaptchaService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request)
    {
        if (!await VerifyRecaptchaAsync(request.RecaptchaToken))
        {
            return BadRequest(new { message = "Verificación de reCAPTCHA inválida." });
        }

        try
        {
            var response = await _authService.RegisterAsync(request);

            return Ok(response);
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

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request)
    {
        if (!await VerifyRecaptchaAsync(request.RecaptchaToken))
        {
            return BadRequest(new { message = "Verificación de reCAPTCHA inválida." });
        }

        var response = await _authService.LoginAsync(request);

        if (response is null)
        {
            return Unauthorized(new
            {
                message = "Credenciales inválidas."
            });
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("facial")]
    public async Task<IActionResult> LoginFacial(
        [FromBody] FacialLoginRequest request)
    {
        try
        {
            var response = await _authService.LoginFacialAsync(request);

            if (response is null)
            {
                return Unauthorized(new
                {
                    message = "No fue posible verificar tu identidad con esa fotografía."
                });
            }

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpPost("qr")]
    public async Task<IActionResult> LoginByQr(
        [FromBody] QrLoginRequest request)
    {
        var response = await _authService.LoginByQrAsync(request);

        if (response is null)
        {
            return Unauthorized(new
            {
                message = "El código QR no es válido o la credencial ya no está activa."
            });
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request)
    {
        await _authService.RequestPasswordResetAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        // Mensaje genérico siempre, exista o no el correo.
        return Ok(new
        {
            message =
                "Si el correo está registrado, recibirás instrucciones para restablecer tu contraseña."
        });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(request);

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

    /// <summary>Cierra la sesión actual en el servidor (el JWT deja de servir).</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = RequestInfo.GetUserId(User);

        if (userId is null)
        {
            return Unauthorized(new { message = "Usuario no válido." });
        }

        await _authService.LogoutAsync(
            userId.Value,
            SessionService.GetSessionId(User));

        return Ok(new { message = "Sesión cerrada." });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,

            email = User.FindFirst(
                System.Security.Claims.ClaimTypes.Email)?.Value,

            nickname = User.FindFirst("nickname")?.Value,

            role = User.FindFirst(
                System.Security.Claims.ClaimTypes.Role)?.Value
        });
    }

    private Task<bool> VerifyRecaptchaAsync(string? token)
    {
        return _recaptchaService.VerifyAsync(
            token,
            HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}