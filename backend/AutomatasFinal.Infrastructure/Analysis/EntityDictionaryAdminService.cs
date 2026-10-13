using System.Text.RegularExpressions;
using AutomatasFinal.Application.Analysis;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Analysis;

/// <summary>
/// Parametrización del sistema (ADMIN): diccionario de entidades que usa el
/// analizador léxico. Los cambios aplican en el siguiente análisis (el
/// analizador lee el diccionario de la BD en cada ejecución).
/// </summary>
public partial class EntityDictionaryAdminService
{
    // Deben coincidir con el CHECK de entity_dictionary.entity_type en la BD.
    public static readonly string[] EntityTypes =
        { "PERSON", "COUNTRY", "CITY", "ORGANIZATION", "BRAND", "PRODUCT", "LOCATION" };

    public static readonly string[] Languages = Enum.GetNames<AnalysisLanguage>();

    private const int MaxValueLength = 80;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public EntityDictionaryAdminService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<object> ListAsync(string? entityType, string? language, string? search, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.EntityDictionary.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var type = entityType.Trim().ToUpperInvariant();
            query = query.Where(x => x.EntityType == type);
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            var lang = language.Trim().ToUpperInvariant();
            query = lang == "ALL"
                ? query.Where(x => x.Language == null)
                : query.Where(x => x.Language == lang);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(x => EF.Functions.ILike(x.NormalizedValue, pattern));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(x => x.EntityType)
            .ThenBy(x => x.Language)
            .ThenBy(x => x.NormalizedValue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Value,
                x.CanonicalValue,
                x.EntityType,
                x.Language,
                x.Source,
                x.CreatedAt
            })
            .ToListAsync();

        var summary = await _db.EntityDictionary
            .AsNoTracking()
            .GroupBy(x => x.EntityType)
            .Select(g => new { EntityType = g.Key, Count = g.Count() })
            .ToListAsync();

        return new { items, total, page, pageSize, summary };
    }

    public async Task<EntityDictionary> CreateAsync(EntityDictionaryEntryRequest request)
    {
        var value = request.Value?.Trim() ?? string.Empty;

        if (value.Length == 0 || value.Length > MaxValueLength || !SingleWordRegex().IsMatch(value))
        {
            throw new ArgumentException(
                "El valor debe ser una sola palabra (letras, apóstrofo o guion), de hasta 80 caracteres. " +
                "El analizador compara palabra por palabra.");
        }

        var canonical = string.IsNullOrWhiteSpace(request.CanonicalValue)
            ? null
            : request.CanonicalValue.Trim();

        if (canonical is { Length: > 120 })
        {
            throw new ArgumentException("La forma canónica no puede superar 120 caracteres.");
        }

        var type = request.EntityType?.Trim().ToUpperInvariant();

        if (type is null || !EntityTypes.Contains(type))
        {
            throw new ArgumentException("Tipo de entidad no válido.");
        }

        string? language = null;

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            language = request.Language.Trim().ToUpperInvariant();

            if (!Languages.Contains(language))
            {
                throw new ArgumentException("Idioma no válido.");
            }
        }

        // Misma normalización que usa TextAnalyzer al comparar.
        var normalized = value.ToLowerInvariant();

        var exists = await _db.EntityDictionary.AnyAsync(x =>
            x.NormalizedValue == normalized &&
            x.EntityType == type &&
            x.Language == language);

        if (exists)
        {
            throw new InvalidOperationException("Esa entrada ya existe para ese tipo e idioma.");
        }

        var entry = new EntityDictionary
        {
            Id = Guid.NewGuid(),
            Value = value,
            NormalizedValue = normalized,
            CanonicalValue = canonical,
            EntityType = type,
            Language = language,
            Source = "admin",
            CreatedAt = DateTime.UtcNow
        };

        _db.EntityDictionary.Add(entry);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            AuditActions.DictionaryEntryCreated,
            true,
            details: new { entry.Id, entry.Value, entry.EntityType, entry.Language, entry.CanonicalValue });

        return entry;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entry = await _db.EntityDictionary.FirstOrDefaultAsync(x => x.Id == id);

        if (entry is null)
        {
            return false;
        }

        _db.EntityDictionary.Remove(entry);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            AuditActions.DictionaryEntryDeleted,
            true,
            details: new { entry.Id, entry.Value, entry.EntityType, entry.Language, entry.Source });

        return true;
    }

    [GeneratedRegex(@"^[\p{L}\p{M}][\p{L}\p{M}'\-]*$")]
    private static partial Regex SingleWordRegex();
}
