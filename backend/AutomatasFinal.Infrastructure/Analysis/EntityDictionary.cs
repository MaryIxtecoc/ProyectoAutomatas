namespace AutomatasFinal.Domain.Entities;

public class EntityDictionary
{
    public Guid Id { get; set; }

    public string Value { get; set; } = null!;

    public string NormalizedValue { get; set; } = null!;

    public string? CanonicalValue { get; set; }

    public string EntityType { get; set; } = null!;

    public string? Language { get; set; }

    public string? Source { get; set; }

    public DateTime CreatedAt { get; set; }
}