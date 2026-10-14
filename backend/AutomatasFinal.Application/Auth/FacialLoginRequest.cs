namespace AutomatasFinal.Application.Auth;

public class FacialLoginRequest
{
    /// <summary>Correo del usuario contra el cual se hace la verificación 1 a 1.</summary>
    public string Email { get; set; } = null!;

    /// <summary>Foto tomada en el momento del login, en Base64 puro (sin prefijo data:).</summary>
    public string PhotoBase64 { get; set; } = null!;
}
