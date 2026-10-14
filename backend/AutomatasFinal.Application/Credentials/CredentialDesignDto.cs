namespace AutomatasFinal.Application.Credentials;

/// <summary>
/// Apariencia de la credencial. No contiene ningún dato de identidad: el
/// backend ignora cualquier otra cosa y usa siempre los datos reales del usuario.
/// </summary>
public class CredentialDesignDto
{
    public string Template { get; set; } = "CLASSIC";
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public string AccentColor { get; set; } = "#0A162A";
    public string TextColor { get; set; } = "#101828";
    public string PhotoStyle { get; set; } = "SQUARE";
    public string QrStyle { get; set; } = "CLASSIC";
    public List<CredentialDecorationDto> Decorations { get; set; } = new();
}

/// <summary>Sticker colocado sobre la credencial (posición relativa 0..1).</summary>
public class CredentialDecorationDto
{
    /// <summary>GLOBAL (stickers/global) o USER (stickers propios del usuario).</summary>
    public string Source { get; set; } = "GLOBAL";
    public string File { get; set; } = null!;
    /// <summary>Centro horizontal, fracción del ancho (0..1).</summary>
    public double X { get; set; }
    /// <summary>Centro vertical, fracción del alto (0..1).</summary>
    public double Y { get; set; }
    /// <summary>Lado del sticker, fracción del ancho de la credencial.</summary>
    public double Size { get; set; }
    /// <summary>Grados (-180..180).</summary>
    public double Rotation { get; set; }
}
