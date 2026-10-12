namespace AutomatasFinal.Domain.Entities;

public class AnalysisJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string FileName { get; set; } = null!;
    public string FileStoragePath { get; set; } = null!;
    public AnalysisLanguage Language { get; set; }
    public AnalysisStatus Status { get; set; }
    public bool ReportRequested { get; set; }
    public string? ReportPdfPath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<AnalysisResult> Results { get; set; } = new List<AnalysisResult>();
}