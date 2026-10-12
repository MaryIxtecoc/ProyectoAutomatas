namespace AutomatasFinal.Domain.Entities;

public class UserCredential
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string QrCodeValue { get; set; } = null!;

    public string? PdfPath { get; set; }

    public bool IsActive { get; set; }

    public DateTime IssuedAt { get; set; }

    public User User { get; set; } = null!;
}

