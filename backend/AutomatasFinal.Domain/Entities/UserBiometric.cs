namespace AutomatasFinal.Domain.Entities;

public class UserBiometric
{
    public Guid UserId { get; set; }

    public string FaceDescriptor { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
