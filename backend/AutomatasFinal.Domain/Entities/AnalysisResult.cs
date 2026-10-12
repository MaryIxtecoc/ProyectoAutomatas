namespace AutomatasFinal.Domain.Entities;

public class AnalysisResult
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public int TotalWordCount { get; set; }

    public string? MostFrequentWords { get; set; }

    public string? LeastFrequentWords { get; set; }

    public string? PersonalPronouns { get; set; }

    public string? PersonNames { get; set; }

    public string? RootNouns { get; set; }

    public string? RootVerbs { get; set; }

    public string? OtherClassifications { get; set; }

    public string FullResult { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public AnalysisJob Job { get; set; } = null!;
}
