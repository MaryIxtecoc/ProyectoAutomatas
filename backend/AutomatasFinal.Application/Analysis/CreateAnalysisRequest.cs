namespace AutomatasFinal.Application.Analysis;

public class CreateAnalysisRequest
{
    public string Language { get; set; } = null!;
    public bool ReportRequested { get; set; }
}