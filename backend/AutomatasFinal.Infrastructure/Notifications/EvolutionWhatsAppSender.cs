using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutomatasFinal.Infrastructure.Notifications;

/// <summary>
/// Envía mensajes de WhatsApp usando Evolution API (la que está desplegada en Render).
///
/// Configuración (appsettings.json, sección "WhatsAppApi"):
///   BaseUrl  -> https://evolution-api-latest-z2ry.onrender.com
///   ApiKey   -> se manda en el header "apikey"
///   Instance -> nombre de la instancia conectada a WhatsApp (ej. "mi-whatsapp")
///
/// - Sin adjunto: POST {BaseUrl}/message/sendText/{Instance}  { number, text }
/// - Con PDF:     POST {BaseUrl}/message/sendMedia/{Instance} { number, mediatype, mimetype, caption, media, fileName }
///   "media" puede ser una URL pública (o firmada) o el PDF en base64.
/// </summary>
public class EvolutionWhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EvolutionWhatsAppSender> _logger;

    public EvolutionWhatsAppSender(
        HttpClient http,
        IConfiguration configuration,
        ILogger<EvolutionWhatsAppSender> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(
        string toPhone,
        string body,
        string? mediaUrl = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["WhatsAppApi:BaseUrl"]?.TrimEnd('/');
        var apiKey = _configuration["WhatsAppApi:ApiKey"];
        var instance = _configuration["WhatsAppApi:Instance"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(instance))
        {
            _logger.LogWarning(
                "Evolution API no está configurada (WhatsAppApi:BaseUrl/ApiKey/Instance). No se envió el WhatsApp a {ToPhone}.",
                toPhone);
            return;
        }

        var number = NormalizePhone(toPhone);
        var instancePath = Uri.EscapeDataString(instance);

        HttpRequestMessage request;

        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/message/sendText/{instancePath}")
            {
                Content = JsonContent.Create(new { number, text = body })
            };
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/message/sendMedia/{instancePath}")
            {
                Content = JsonContent.Create(new
                {
                    number,
                    mediatype = "document",
                    mimetype = "application/pdf",
                    caption = body,
                    media = mediaUrl,
                    fileName = GuessFileName(mediaUrl)
                })
            };
        }

        request.Headers.Add("apikey", apiKey);

        using (request)
        using (var response = await _http.SendAsync(request, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Evolution API respondió {(int)response.StatusCode} al enviar WhatsApp a {number}: {error}");
            }
        }

        _logger.LogInformation("WhatsApp enviado a {ToPhone} por Evolution API", number);
    }

    /// <summary>
    /// Evolution API quiere solo dígitos con código de país, sin "+" (ej. 50245113021).
    /// Si el número viene sin código de país (8 dígitos), se asume Guatemala (502).
    /// </summary>
    public static string NormalizePhone(string phone)
    {
        var digits = Regex.Replace(phone ?? "", @"\D", "");

        if (digits.Length == 8)
        {
            digits = "502" + digits;
        }

        return digits;
    }

    /// <summary>
    /// Saca el nombre del archivo de la URL (sin el "?token=..." de las URLs firmadas).
    /// </summary>
    public static string GuessFileName(string mediaUrl)
    {
        if (Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
            }
        }

        return "documento.pdf";
    }
}
