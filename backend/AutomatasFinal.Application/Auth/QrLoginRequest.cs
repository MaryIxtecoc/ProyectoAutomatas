namespace AutomatasFinal.Application.Auth;

public class QrLoginRequest
{
    /// <summary>Valor leído del código QR de la credencial del usuario.</summary>
    public string QrCodeValue { get; set; } = null!;
}
