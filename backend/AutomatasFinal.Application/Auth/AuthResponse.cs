namespace AutomatasFinal.Application.Auth;

public class AuthResponse
{
    public string Token { get; set; } = null!;
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string Nickname { get; set; } = null!;
    public short RoleId { get; set; }
}