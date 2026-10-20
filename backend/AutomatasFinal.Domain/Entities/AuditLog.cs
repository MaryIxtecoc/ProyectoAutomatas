namespace AutomatasFinal.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    /// <summary>Quién hizo la acción (null si fue anónima, p. ej. login fallido).</summary>
    public Guid? UserId { get; set; }
    /// <summary>Usuario afectado por la acción.</summary>
    public Guid? TargetUserId { get; set; }
    /// <summary>Resultado de la acción.</summary>
    public bool? Success { get; set; }
    public string Action { get; set; } = null!;
    public string? Details { get; set; }
    public System.Net.IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}