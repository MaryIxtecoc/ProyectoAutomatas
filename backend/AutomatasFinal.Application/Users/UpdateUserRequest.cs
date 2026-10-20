namespace AutomatasFinal.Application.Users;

public class UpdateUserRequest
{
    public string Email { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public DateOnly BirthDate { get; set; }

    public string Nickname { get; set; } = null!;

    public string NotificationMethod { get; set; } = null!;
}