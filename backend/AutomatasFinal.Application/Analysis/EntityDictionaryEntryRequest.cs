namespace AutomatasFinal.Application.Analysis;

/// <summary>Alta de una entrada del diccionario de entidades (solo ADMIN).</summary>
public class EntityDictionaryEntryRequest
{
    public string Value { get; set; } = null!;
    public string? CanonicalValue { get; set; }
    public string EntityType { get; set; } = null!;
    /// <summary>ES, EN, RU, ZH, AR o null (todos los idiomas).</summary>
    public string? Language { get; set; }
}
