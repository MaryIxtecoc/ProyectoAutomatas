using AutomatasFinal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Analysis;

public class EntityDictionaryService
{
    private readonly AppDbContext _db;

    public EntityDictionaryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<EntityDictionaryEntry>> GetAllAsync(
        string? language = null)
    {
        var query = _db.EntityDictionary
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(language))
        {
            query = query.Where(x =>
                x.Language == null ||
                x.Language == language);
        }

        return await query
            .Select(x => new EntityDictionaryEntry
            {
                Value = x.Value,
                NormalizedValue = x.NormalizedValue,
                CanonicalValue = x.CanonicalValue,
                EntityType = x.EntityType,
                Language = x.Language
            })
            .ToListAsync();
    }

    public async Task<HashSet<string>> GetEntitiesAsync(
        string entityType,
        string? language = null)
    {
        var query = _db.EntityDictionary
            .AsNoTracking()
            .Where(x => x.EntityType == entityType);

        if (!string.IsNullOrWhiteSpace(language))
        {
            query = query.Where(x =>
                x.Language == null ||
                x.Language == language);
        }

        var values = await query
            .Select(x => x.NormalizedValue)
            .ToListAsync();

        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public class EntityDictionaryEntry
{
    public string Value { get; set; } = string.Empty;

    public string NormalizedValue { get; set; } = string.Empty;

    public string? CanonicalValue { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string? Language { get; set; }
}