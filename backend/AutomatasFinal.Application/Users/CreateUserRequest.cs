namespace AutomatasFinal.Application.Users;

public class CreateUserRequest
{
    public string Email { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public DateOnly BirthDate { get; set; }

    public string Nickname { get; set; } = null!;

    public string Password { get; set; } = null!;

    public short RoleId { get; set; }

    public string NotificationMethod { get; set; } = null!;
}