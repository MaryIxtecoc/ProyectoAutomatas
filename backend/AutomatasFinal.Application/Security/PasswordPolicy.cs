namespace AutomatasFinal.Application.Security;

/// <summary>
/// Regla única de contraseñas para todos los endpoints que las reciben
/// (registro, alta por ADMIN, restablecimiento y cambio). No depende de que
/// el frontend valide.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static void Validate(string? password, string fieldName = "La contraseña")
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < MinLength ||
            password.Length > MaxLength)
        {
            throw new ArgumentException(
                $"{fieldName} debe tener entre {MinLength} y {MaxLength} caracteres.");
        }
    }
}
