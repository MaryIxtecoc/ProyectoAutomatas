using System.Text.RegularExpressions;

namespace AutomatasFinal.Application.Security;

/// <summary>
/// Regla única para números de teléfono en todos los endpoints que los reciben
/// (registro, alta y edición por ADMIN, edición de perfil). No depende de que
/// el frontend valide.
/// Acepta solo dígitos, con un "+" opcional al inicio y espacios o guiones
/// como separadores (ej.: "5555-5555", "+502 5555 5555"). Rechaza letras y
/// cualquier otro símbolo.
/// </summary>
public static class PhonePolicy
{
    public const int MinDigits = 8;
    public const int MaxDigits = 15; // máximo internacional (E.164)

    private static readonly Regex AllowedFormat =
        new(@"^\+?[0-9]+([ -][0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// Valida el teléfono y lo devuelve limpio (sin espacios sobrantes).
    /// Lanza ArgumentException con un mensaje claro si no es válido.
    /// </summary>
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException("El teléfono es obligatorio.");
        }

        var value = Regex.Replace(phone.Trim(), @"\s+", " ");

        if (!AllowedFormat.IsMatch(value))
        {
            throw new ArgumentException(
                "El teléfono solo puede contener números (se permite + al inicio, espacios o guiones).");
        }

        var digits = value.Count(char.IsAsciiDigit);

        if (digits < MinDigits || digits > MaxDigits)
        {
            throw new ArgumentException(
                $"El teléfono debe tener entre {MinDigits} y {MaxDigits} dígitos.");
        }

        return value;
    }
}
