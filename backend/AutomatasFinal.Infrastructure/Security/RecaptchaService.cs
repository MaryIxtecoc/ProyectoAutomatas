using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutomatasFinal.Infrastructure.Security;

public interface IRecaptchaService
{
    Task<bool> VerifyAsync(string? token, string? remoteIp = null);
}

public class RecaptchaService : IRecaptchaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecaptchaService> _logger;

    public RecaptchaService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<RecaptchaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(string? token, string? remoteIp = null)
    {
        var secretKey = _configuration["Recaptcha:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            // Todavía no configuraron las llaves reales: no se bloquea el
            // flujo en desarrollo, pero queda visible en el log.
            _logger.LogWarning(
                "Recaptcha:SecretKey no está configurado; se omite la verificación de reCAPTCHA.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parameters = new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            parameters["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await _httpClient.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(parameters));

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<RecaptchaVerifyResponse>();

            if (body?.Success != true)
            {
                _logger.LogWarning(
                    "Verificación de reCAPTCHA rechazada: {Errors}",
                    body?.ErrorCodes is null ? "sin detalle" : string.Join(", ", body.ErrorCodes));
            }

            return body?.Success == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible verificar el token de reCAPTCHA.");
            return false;
        }
    }

    private class RecaptchaVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string>? ErrorCodes { get; set; }
    }
}
