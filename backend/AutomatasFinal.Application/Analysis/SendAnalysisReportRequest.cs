namespace AutomatasFinal.Application.Analysis;

public class SendAnalysisReportRequest
{
    /// <summary>Canal por el que se debe enviar el reporte: "EMAIL" o "WHATSAPP".</summary>
    public string Channel { get; set; } = null!;
}
