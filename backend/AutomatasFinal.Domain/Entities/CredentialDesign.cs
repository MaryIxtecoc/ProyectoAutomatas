namespace AutomatasFinal.Domain.Entities;

/// <summary>
/// Apariencia de la credencial elegida por el usuario. Solo estilo: la
/// identidad (nombre, email, rol, foto, QR) siempre sale de la BD.
/// </summary>
public class CredentialDesign
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Template { get; set; } = null!;
    public string? BackgroundColor { get; set; }
    public string? AccentColor { get; set; }
    public string? TextColor { get; set; }
    public string? PhotoStyle { get; set; }
    public string? QrStyle { get; set; }
    /// <summary>jsonb: lista de stickers colocados.</summary>
    public string? Decorations { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
