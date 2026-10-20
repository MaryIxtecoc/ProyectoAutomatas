using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutomatasFinal.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly IEmailSender _emailSender;
    private readonly IWhatsAppSender _whatsAppSender;
    private readonly SupabaseStorageService _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IEmailSender emailSender,
        IWhatsAppSender whatsAppSender,
        SupabaseStorageService storage,
        IConfiguration configuration,
        ILogger<NotificationService> logger)
    {
        _emailSender = emailSender;
        _whatsAppSender = whatsAppSender;
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendCredentialAsync(User user, byte[] pdfBytes, string pdfBucket, string pdfPath)
    {
        const string subject = "Tu credencial de AutomatasFinal";

        var html =
            $"<p>Hola {Escape(user.Nickname)},</p>" +
            "<p>Gracias por registrarte en AutomatasFinal. Adjuntamos tu credencial de inscripción " +
            "con tu código QR.</p>" +
            "<p>Puedes usarla para iniciar sesión leyendo el QR desde la página de login.</p>";

        var whatsAppBody =
            $"¡Bienvenido/a a Léxica, {user.Nickname}! 🎉 Tu registro quedó listo. " +
            "Te adjuntamos tu credencial en PDF; puedes usarla para iniciar sesión leyendo su código QR.";

        return SendByUserChannelAsync(
            user,
            () => _emailSender.SendAsync(
                user.Email,
                user.Nickname,
                subject,
                html,
                new[] { new EmailAttachment("credencial.pdf", pdfBytes, "application/pdf") }),
            async () =>
            {
                var url = await TryGetSignedUrlAsync(pdfBucket, pdfPath);
                await _whatsAppSender.SendAsync(user.Phone, whatsAppBody, url);
            });
    }

    public Task SendPasswordResetAsync(User user, string rawToken)
    {
        var frontendUrl = (_configuration["App:FrontendBaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
        var link = $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        const string subject = "Restablece tu contraseña - AutomatasFinal";

        var html =
            $"<p>Hola {Escape(user.Nickname)},</p>" +
            "<p>Solicitaste restablecer tu contraseña. Este enlace vence en 30 minutos:</p>" +
            $"<p><a href=\"{link}\">{link}</a></p>" +
            "<p>Si tú no lo solicitaste, puedes ignorar este mensaje.</p>";

        var whatsAppBody =
            $"Hola {user.Nickname}, para restablecer tu contraseña entra a: {link} (vence en 30 minutos).";

        return SendByUserChannelAsync(
            user,
            () => _emailSender.SendAsync(user.Email, user.Nickname, subject, html),
            () => _whatsAppSender.SendAsync(user.Phone, whatsAppBody));
    }

    public async Task SendAnalysisReportAsync(
        User user,
        byte[] pdfBytes,
        string pdfBucket,
        string pdfPath,
        string fileName,
        NotificationMethod channel)
    {
        const string subject = "Tu reporte de análisis léxico - AutomatasFinal";

        var html =
            $"<p>Hola {Escape(user.Nickname)},</p>" +
            "<p>Adjuntamos el reporte en PDF del análisis que solicitaste.</p>";

        var whatsAppBody =
            $"Hola {user.Nickname}, aquí está tu reporte de análisis léxico en PDF.";

        var tasks = new List<Task>();

        if (channel is NotificationMethod.EMAIL or NotificationMethod.BOTH)
        {
            tasks.Add(_emailSender.SendAsync(
                user.Email,
                user.Nickname,
                subject,
                html,
                new[] { new EmailAttachment(fileName, pdfBytes, "application/pdf") }));
        }

        if (channel is NotificationMethod.WHATSAPP or NotificationMethod.BOTH)
        {
            tasks.Add(SendReportByWhatsAppAsync(user, pdfBucket, pdfPath, whatsAppBody));
        }

        // Aquí sí dejamos que la excepción suba: el usuario pidió el envío de forma explícita
        // (a diferencia de la credencial o el reset, que son efectos secundarios de otra acción)
        // y necesita saber si falló.
        await Task.WhenAll(tasks);
    }

    private async Task SendReportByWhatsAppAsync(User user, string pdfBucket, string pdfPath, string body)
    {
        var url = await TryGetSignedUrlAsync(pdfBucket, pdfPath);
        await _whatsAppSender.SendAsync(user.Phone, body, url);
    }

    private async Task SendByUserChannelAsync(User user, Func<Task> sendEmail, Func<Task> sendWhatsApp)
    {
        var tasks = new List<Task>();

        if (user.NotificationMethod is NotificationMethod.EMAIL or NotificationMethod.BOTH)
        {
            tasks.Add(RunSafely(sendEmail, "email", user.Email));
        }

        if (user.NotificationMethod is NotificationMethod.WHATSAPP or NotificationMethod.BOTH)
        {
            tasks.Add(RunSafely(sendWhatsApp, "whatsapp", user.Phone));
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunSafely(Func<Task> action, string channel, string destination)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            // No se relanza: una notificación fallida (p. ej. SMTP no configurado en desarrollo)
            // no debe tumbar el flujo principal (registro, reset de contraseña, etc.).
            _logger.LogError(
                ex,
                "No fue posible enviar la notificación por {Channel} a {Destination}.",
                channel,
                destination);
        }
    }

    private async Task<string?> TryGetSignedUrlAsync(string bucket, string path)
    {
        try
        {
            return await _storage.CreateSignedUrlAsync(bucket, path, 3600);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible generar la URL firmada para {Bucket}/{Path}.", bucket, path);
            return null;
        }
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
