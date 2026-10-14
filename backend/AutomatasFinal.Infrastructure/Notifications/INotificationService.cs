using AutomatasFinal.Domain.Entities;

namespace AutomatasFinal.Infrastructure.Notifications;

public interface INotificationService
{
    /// <summary>Envía la credencial (PDF con QR) recién emitida, según el canal del usuario.</summary>
    Task SendCredentialAsync(User user, byte[] pdfBytes, string pdfBucket, string pdfPath);

    /// <summary>Envía el enlace para restablecer contraseña, según el canal del usuario.</summary>
    Task SendPasswordResetAsync(User user, string rawToken);

    /// <summary>Envía el reporte de análisis por el canal explícitamente solicitado por el usuario.</summary>
    Task SendAnalysisReportAsync(
        User user,
        byte[] pdfBytes,
        string pdfBucket,
        string pdfPath,
        string fileName,
        NotificationMethod channel);
}
