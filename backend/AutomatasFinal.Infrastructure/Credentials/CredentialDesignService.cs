using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutomatasFinal.Application.Credentials;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Data;
using AutomatasFinal.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OpenCvSharp;

namespace AutomatasFinal.Infrastructure.Credentials;

/// <summary>
/// Diseño (solo apariencia) de la credencial y stickers propios del usuario.
/// Todo lo que llega del frontend se valida aquí; nada de identidad se acepta.
/// </summary>
public partial class CredentialDesignService
{
    public const string StickersBucket = "stickers";
    public const string GlobalStickersFolder = "global";
    public const int MaxDecorations = 12;
    public const int MaxUserStickers = 20;
    public const long MaxStickerBytes = 1024 * 1024;
    public const int MaxStickerSide = 1024;

    public static readonly string[] Templates = { "CLASSIC", "LATERAL", "MINIMAL" };
    public static readonly string[] PhotoStyles = { "SQUARE", "ROUNDED", "CIRCLE" };
    public static readonly string[] QrStyles = { "CLASSIC", "ACCENT" };
    public static readonly string[] DecorationSources = { "GLOBAL", "USER" };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly SupabaseStorageService _storage;
    private readonly AuditService _audit;

    public CredentialDesignService(
        AppDbContext db,
        SupabaseStorageService storage,
        AuditService audit)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
    }

    // ------------------------------------------------------------------
    // Diseño
    // ------------------------------------------------------------------

    /// <summary>Diseño guardado del usuario, o el diseño por defecto.</summary>
    public async Task<CredentialDesignDto> GetForUserAsync(Guid userId)
    {
        var design = await _db.CredentialDesigns
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync();

        if (design is null)
        {
            return new CredentialDesignDto();
        }

        var dto = new CredentialDesignDto
        {
            Template = design.Template,
            BackgroundColor = design.BackgroundColor ?? "#FFFFFF",
            AccentColor = design.AccentColor ?? "#0A162A",
            TextColor = design.TextColor ?? "#101828",
            PhotoStyle = design.PhotoStyle ?? "SQUARE",
            QrStyle = design.QrStyle ?? "CLASSIC",
            Decorations = ParseDecorations(design.Decorations)
        };

        // Lo guardado pudo quedar inválido (p. ej. un sticker que ya no existe
        // en el catálogo): si no valida, se usa el diseño por defecto.
        try
        {
            return Validate(dto);
        }
        catch (ArgumentException)
        {
            return new CredentialDesignDto();
        }
    }

    public async Task<CredentialDesignDto> SaveAsync(Guid userId, CredentialDesignDto input)
    {
        var dto = Validate(input);

        var designs = await _db.CredentialDesigns
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync();

        var design = designs.FirstOrDefault();
        var now = DateTime.UtcNow;

        if (design is null)
        {
            design = new CredentialDesign
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = now
            };

            _db.CredentialDesigns.Add(design);
        }

        // Si por algún motivo hubiera más de un diseño, se deja solo uno.
        _db.CredentialDesigns.RemoveRange(designs.Skip(1));

        design.Template = dto.Template;
        design.BackgroundColor = dto.BackgroundColor;
        design.AccentColor = dto.AccentColor;
        design.TextColor = dto.TextColor;
        design.PhotoStyle = dto.PhotoStyle;
        design.QrStyle = dto.QrStyle;
        design.Decorations = JsonSerializer.Serialize(dto.Decorations, JsonOptions);
        design.UpdatedAt = now;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            AuditActions.CredentialDesignUpdated,
            true,
            userId,
            details: new
            {
                dto.Template,
                dto.BackgroundColor,
                dto.AccentColor,
                dto.TextColor,
                dto.PhotoStyle,
                dto.QrStyle,
                decorations = dto.Decorations.Count
            });

        return dto;
    }

    /// <summary>Vuelve al diseño por defecto (borra el diseño guardado).</summary>
    public async Task ResetAsync(Guid userId)
    {
        var deleted = await _db.CredentialDesigns
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();

        if (deleted > 0)
        {
            await _audit.LogAsync(AuditActions.CredentialDesignReset, true, userId);
        }
    }

    /// <summary>
    /// Valida y normaliza un diseño. Lanza ArgumentException con un mensaje
    /// apto para el usuario si algo no es válido.
    /// </summary>
    public static CredentialDesignDto Validate(CredentialDesignDto? input)
    {
        if (input is null)
        {
            throw new ArgumentException("El diseño es obligatorio.");
        }

        var dto = new CredentialDesignDto
        {
            Template = OneOf(input.Template, Templates, "La plantilla no es válida."),
            BackgroundColor = HexColor(input.BackgroundColor, "El color de fondo no es válido."),
            AccentColor = HexColor(input.AccentColor, "El color de acento no es válido."),
            TextColor = HexColor(input.TextColor, "El color del texto no es válido."),
            PhotoStyle = OneOf(input.PhotoStyle, PhotoStyles, "El estilo de foto no es válido."),
            QrStyle = OneOf(input.QrStyle, QrStyles, "El estilo de QR no es válido.")
        };

        if (ContrastRatio(dto.TextColor, dto.BackgroundColor) < 3)
        {
            throw new ArgumentException(
                "El color del texto no contrasta lo suficiente con el fondo.");
        }

        // El QR se imprime oscuro sobre blanco; si el acento es claro no se podría escanear.
        if (dto.QrStyle == "ACCENT" && ContrastRatio(dto.AccentColor, "#FFFFFF") < 4.5)
        {
            throw new ArgumentException(
                "El color de acento es muy claro para el QR. Usa el QR clásico o un acento más oscuro.");
        }

        var decorations = input.Decorations ?? new List<CredentialDecorationDto>();

        if (decorations.Count > MaxDecorations)
        {
            throw new ArgumentException($"Puedes colocar como máximo {MaxDecorations} stickers.");
        }

        foreach (var d in decorations)
        {
            if (d is null)
            {
                throw new ArgumentException("Sticker inválido.");
            }

            var source = OneOf(d.Source, DecorationSources, "Origen de sticker inválido.");

            if (string.IsNullOrWhiteSpace(d.File) || !StickerFileRegex().IsMatch(d.File))
            {
                throw new ArgumentException("Nombre de sticker inválido.");
            }

            if (!double.IsFinite(d.X) || !double.IsFinite(d.Y) ||
                !double.IsFinite(d.Size) || !double.IsFinite(d.Rotation))
            {
                throw new ArgumentException("Posición de sticker inválida.");
            }

            dto.Decorations.Add(new CredentialDecorationDto
            {
                Source = source,
                File = d.File,
                X = Math.Round(Math.Clamp(d.X, 0, 1), 4),
                Y = Math.Round(Math.Clamp(d.Y, 0, 1), 4),
                Size = Math.Round(Math.Clamp(d.Size, 0.06, 0.45), 4),
                Rotation = Math.Round(Math.Clamp(d.Rotation, -180, 180), 1)
            });
        }

        return dto;
    }

    /// <summary>
    /// Descarga los stickers del diseño para dibujarlos en el PDF. La carpeta
    /// de los stickers USER se arma con el userId del servidor: nunca se usa
    /// una ruta enviada por el cliente. Los que ya no existen se omiten.
    /// </summary>
    public async Task<List<(CredentialDecorationDto Decoration, byte[] Bytes)>> LoadDecorationImagesAsync(
        Guid userId,
        CredentialDesignDto design)
    {
        var result = new List<(CredentialDecorationDto, byte[])>();

        foreach (var decoration in design.Decorations)
        {
            try
            {
                var bytes = await _storage.DownloadAsync(
                    StickersBucket,
                    StickerPath(userId, decoration.Source, decoration.File));

                // Se normaliza a PNG: si no es una imagen válida se omite en vez
                // de romper la generación del PDF.
                using var image = Cv2.ImDecode(bytes, ImreadModes.Unchanged);

                if (!image.Empty())
                {
                    Cv2.ImEncode(".png", image, out var png);
                    result.Add((decoration, png));
                }
            }
            catch
            {
                // Sticker borrado o inaccesible: la credencial se genera sin él.
            }
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Stickers
    // ------------------------------------------------------------------

    public async Task<object> ListStickersAsync(Guid userId)
    {
        var global = await SafeListAsync(GlobalStickersFolder);
        var mine = await SafeListAsync(UserFolder(userId));

        return new
        {
            global = global.Where(IsAllowedStickerName).OrderBy(x => x).ToList(),
            mine = mine.Where(IsAllowedStickerName).OrderBy(x => x).ToList(),
            maxUserStickers = MaxUserStickers
        };
    }

    public async Task<byte[]?> GetUserStickerAsync(Guid userId, string file)
    {
        if (!IsAllowedStickerName(file))
        {
            return null;
        }

        try
        {
            return await _storage.DownloadAsync(StickersBucket, $"{UserFolder(userId)}/{file}");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sube un sticker propio. La imagen se decodifica (si no es una imagen
    /// real se rechaza) y se vuelve a codificar como PNG: así se descartan
    /// metadatos y contenido ajeno a la imagen.
    /// </summary>
    public async Task<string> UploadUserStickerAsync(Guid userId, IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            throw new ArgumentException("Selecciona una imagen.");
        }

        if (file.Length > MaxStickerBytes)
        {
            throw new ArgumentException("El sticker no puede superar 1 MB.");
        }

        var existing = await SafeListAsync(UserFolder(userId));

        if (existing.Count(IsAllowedStickerName) >= MaxUserStickers)
        {
            throw new ArgumentException($"Puedes tener como máximo {MaxUserStickers} stickers propios.");
        }

        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory);

        using var image = Cv2.ImDecode(memory.ToArray(), ImreadModes.Unchanged);

        if (image.Empty())
        {
            throw new ArgumentException("El archivo no es una imagen válida (PNG, JPG o WEBP).");
        }

        using var resized = FitInside(image, MaxStickerSide);

        Cv2.ImEncode(".png", resized, out var pngBytes);

        var name = $"{Guid.NewGuid():N}.png";

        await using (var stream = new MemoryStream(pngBytes))
        {
            await _storage.UploadAsync(StickersBucket, $"{UserFolder(userId)}/{name}", stream, "image/png");
        }

        await _audit.LogAsync(
            AuditActions.StickerUploaded,
            true,
            userId,
            details: new { file = name, width = resized.Width, height = resized.Height });

        return name;
    }

    public async Task<bool> DeleteUserStickerAsync(Guid userId, string file)
    {
        if (!IsAllowedStickerName(file))
        {
            return false;
        }

        var existing = await SafeListAsync(UserFolder(userId));

        if (!existing.Contains(file))
        {
            return false;
        }

        await _storage.DeleteAsync(StickersBucket, $"{UserFolder(userId)}/{file}");

        await _audit.LogAsync(AuditActions.StickerDeleted, true, userId, details: new { file });

        return true;
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    public static string StickerPath(Guid userId, string source, string file) =>
        source == "USER"
            ? $"{UserFolder(userId)}/{file}"
            : $"{GlobalStickersFolder}/{file}";

    private static string UserFolder(Guid userId) => $"users/{userId}";

    private async Task<List<string>> SafeListAsync(string folder)
    {
        try
        {
            return await _storage.ListAsync(StickersBucket, folder);
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool IsAllowedStickerName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && StickerFileRegex().IsMatch(name);

    private static List<CredentialDecorationDto> ParseDecorations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<CredentialDecorationDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<CredentialDecorationDto>>(json, JsonOptions)
                ?? new List<CredentialDecorationDto>();
        }
        catch (JsonException)
        {
            return new List<CredentialDecorationDto>();
        }
    }

    private static Mat FitInside(Mat image, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(image.Width, image.Height));

        if (scale >= 1.0)
        {
            return image.Clone();
        }

        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(), scale, scale, InterpolationFlags.Area);
        return resized;
    }

    private static string OneOf(string? value, string[] allowed, string error)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (normalized is null || !allowed.Contains(normalized))
        {
            throw new ArgumentException(error);
        }

        return normalized;
    }

    private static string HexColor(string? value, string error)
    {
        var normalized = value?.Trim();

        if (normalized is null || !HexColorRegex().IsMatch(normalized))
        {
            throw new ArgumentException(error);
        }

        return normalized.ToUpperInvariant();
    }

    /// <summary>Relación de contraste WCAG entre dos colores #RRGGBB (1..21).</summary>
    public static double ContrastRatio(string hex1, string hex2)
    {
        var l1 = RelativeLuminance(hex1);
        var l2 = RelativeLuminance(hex2);

        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    public static double RelativeLuminance(string hex)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    // Solo nombres simples: sin rutas, sin "..", extensiones de imagen.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-]{0,80}\.(png|webp|jpg|jpeg)$", RegexOptions.IgnoreCase)]
    private static partial Regex StickerFileRegex();
}
