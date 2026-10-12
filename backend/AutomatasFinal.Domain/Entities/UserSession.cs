namespace AutomatasFinal.Domain.Entities;

/// <summary>
/// Una sesión por cada inicio de sesión. El JWT lleva su Id (claim
/// "session_id"); si la sesión se revoca, el JWT deja de ser válido.
/// No guarda el JWT ni ningún secreto.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>PASSWORD, FACE, QR o REGISTER.</summary>
    public string LoginMethod { get; set; } = null!;
    public System.Net.IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }

    public User User { get; set; } = null!;
}
