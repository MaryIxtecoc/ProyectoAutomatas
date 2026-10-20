namespace AutomatasFinal.Domain.Entities;

/// <summary>
/// Solicitud de corrección de email y/o fecha de nacimiento. No se aplica
/// hasta que un ADMIN la aprueba.
/// </summary>
public class ProfileChangeRequest
{
    public const string StatusPending = "PENDING";
    public const string StatusApproved = "APPROVED";
    public const string StatusRejected = "REJECTED";

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? RequestedEmail { get; set; }
    public DateOnly? RequestedBirthDate { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = StatusPending;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public User? Reviewer { get; set; }
}
