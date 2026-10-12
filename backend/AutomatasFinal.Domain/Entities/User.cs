namespace AutomatasFinal.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public DateOnly BirthDate { get; set; }

    public string Nickname { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public short RoleId { get; set; }

    public NotificationMethod NotificationMethod { get; set; }

    public string? PhotoOriginalPath { get; set; }

    public string? PhotoModifiedPath { get; set; }

    public bool IsActive { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Role Role { get; set; } = null!;

    public UserBiometric? Biometric { get; set; }

    public ICollection<UserCredential> Credentials { get; set; } = new List<UserCredential>();

    public ICollection<AnalysisJob> AnalysisJobs { get; set; } = new List<AnalysisJob>();

    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}