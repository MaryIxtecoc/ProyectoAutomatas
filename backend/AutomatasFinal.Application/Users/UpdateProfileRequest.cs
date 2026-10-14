namespace AutomatasFinal.Application.Users;

public class UpdateProfileRequest
{
    public string Phone { get; set; } = null!;

    public string Nickname { get; set; } = null!;

    public string NotificationMethod { get; set; } = null!;
}
