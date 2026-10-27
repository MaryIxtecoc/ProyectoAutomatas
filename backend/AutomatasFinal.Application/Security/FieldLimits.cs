namespace AutomatasFinal.Application.Security;

/// <summary>
/// Largos máximos de los campos de texto que escribe el usuario. Deben coincidir
/// con los CHECK *_len_chk de database/database.sql: si el backend los valida
/// primero, el usuario recibe un mensaje claro y no un error de la base de datos.
/// </summary>
public static class FieldLimits
{
    public const int NicknameMinLength = 3;
    public const int NicknameMaxLength = 30;
    public const int EmailMaxLength = 254;
    public const int FileNameMaxLength = 255;

    public static string NormalizeNickname(string? nickname)
    {
        var value = nickname?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            throw new ArgumentException("El nombre de usuario es obligatorio.");
        }

        if (value.Length < NicknameMinLength || value.Length > NicknameMaxLength)
        {
            throw new ArgumentException(
                $"El nombre de usuario debe tener entre {NicknameMinLength} y {NicknameMaxLength} caracteres.");
        }

        return value;
    }

    public static void EnsureEmailLength(string email)
    {
        if (email.Length > EmailMaxLength)
        {
            throw new ArgumentException(
                $"El correo electrónico no puede superar {EmailMaxLength} caracteres.");
        }
    }

    public static void EnsureFileNameLength(string fileName)
    {
        if (fileName.Length > FileNameMaxLength)
        {
            throw new ArgumentException(
                $"El nombre del archivo no puede superar {FileNameMaxLength} caracteres.");
        }
    }
}
