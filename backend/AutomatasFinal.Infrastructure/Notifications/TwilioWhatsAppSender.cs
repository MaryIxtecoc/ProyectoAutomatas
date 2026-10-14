using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace AutomatasFinal.Infrastructure.Notifications;

public class TwilioWhatsAppSender : IWhatsAppSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TwilioWhatsAppSender> _logger;

    public TwilioWhatsAppSender(IConfiguration configuration, ILogger<TwilioWhatsAppSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(
        string toPhone,
        string body,
        string? mediaUrl = null,
        CancellationToken cancellationToken = default)
    {
        var accountSid = _configuration["Twilio:AccountSid"];
        var authToken = _configuration["Twilio:AuthToken"];
        var whatsAppFrom = _configuration["Twilio:WhatsAppFrom"];

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken) ||
            string.IsNullOrWhiteSpace(whatsAppFrom))
        {
            _logger.LogWarning(
                "Twilio no está configurado (AccountSid/AuthToken/WhatsAppFrom). No se envió el WhatsApp a {ToPhone}.",
                toPhone);
            return;
        }

        TwilioClient.Init(accountSid, authToken);

        var to = new PhoneNumber($"whatsapp:{NormalizePhone(toPhone)}");
        var from = new PhoneNumber(
            whatsAppFrom.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
                ? whatsAppFrom
                : $"whatsapp:{whatsAppFrom}");

        var mediaUrls = mediaUrl is null ? null : new List<Uri> { new(mediaUrl) };

        await MessageResource.CreateAsync(
            to: to,
            from: from,
            body: body,
            mediaUrl: mediaUrls);

        _logger.LogInformation("WhatsApp enviado a {ToPhone}", toPhone);
    }

    /// <summary>
    /// Normaliza el número a formato E.164. Si no trae "+", se asume Guatemala (+502).
    /// Ajusta este valor por defecto si el equipo registra usuarios de otro país.
    /// </summary>
    private static string NormalizePhone(string phone)
    {
        var digits = Regex.Replace(phone, @"[^\d+]", "");
        if (!digits.StartsWith('+'))
        {
            digits = "+502" + digits;
        }

        return digits;
    }
}
