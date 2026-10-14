namespace AutomatasFinal.Application.Auth;

public class RegisterRequest
{
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public string Nickname { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string NotificationMethod { get; set; } = null!;

    /// <summary>
    /// Foto tomada con la cámara, en Base64 puro (sin el prefijo "data:image/...;base64,").
    /// Se usa para el reconocimiento facial (recorte de rostro) y como referencia para el login facial.
    /// </summary>
    public string PhotoOriginalBase64 { get; set; } = null!;

    /// <summary>
    /// Foto con filtro o sticker aplicado en el cliente, en Base64 puro. Es la que se muestra
    /// en la credencial y en la barra de estado del sitio. Si no se envía, se usa la original recortada.
    /// </summary>
    public string? PhotoModifiedBase64 { get; set; }

    /// <summary>Token que entrega el widget de reCAPTCHA en el frontend.</summary>
    public string? RecaptchaToken { get; set; }
}