namespace AutomatasFinal.Application.Auth;

public class LoginRequest
{
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;

    /// <summary>Token que entrega el widget de reCAPTCHA en el frontend.</summary>
    public string? RecaptchaToken { get; set; }
}