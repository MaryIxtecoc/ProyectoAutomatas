using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomatasFinal.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutomatasFinal.Infrastructure.Analysis;

/// <summary>
/// Cliente HTTP del servicio NLP en Python (spaCy para ES/EN/RU/ZH y Stanza
/// para AR, carpeta nlp-service). Nunca lanza excepciones: si el servicio
/// está apagado, tarda demasiado o responde con error, devuelve null y el
/// análisis continúa con el resultado del analizador propio (TextAnalyzer).
///
/// Configuración (appsettings, user-secrets o variables de entorno):
///   NlpService:Enabled         true/false (por defecto true)
///   NlpService:BaseUrl         por defecto http://localhost:8000
///   NlpService:TimeoutSeconds  por defecto 60
/// </summary>
public class NlpServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<NlpServiceClient> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _timeout;

    public NlpServiceClient(
        HttpClient http,
        IConfiguration configuration,
        ILogger<NlpServiceClient> logger)
    {
        _http = http;
        _logger = logger;

        _enabled = !bool.TryParse(configuration["NlpService:Enabled"], out var enabled) || enabled;

        var baseUrl = configuration["NlpService:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:8000";
        }

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var seconds = int.TryParse(configuration["NlpService:TimeoutSeconds"], out var s) && s > 0
            ? s
            : 60;
        _timeout = TimeSpan.FromSeconds(seconds);
        _http.Timeout = Timeout.InfiniteTimeSpan; // se controla con el CancellationToken
    }

    public bool IsEnabled => _enabled;

    public async Task<NlpAnalysisResult?> AnalyzeAsync(
        string text,
        AnalysisLanguage language,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "analyze",
                new { text, language = language.ToString() },
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Servicio NLP respondió {Status} para {Language}; se usa el analizador propio.",
                    (int)response.StatusCode, language);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<NlpAnalysisResult>(JsonOptions, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or OperationCanceledException
                                       or JsonException
                                       or NotSupportedException)
        {
            _logger.LogWarning(
                "Servicio NLP no disponible ({Message}); se usa el analizador propio.",
                ex.Message);
            return null;
        }
    }
}

/// <summary>Respuesta de POST /analyze del servicio NLP.</summary>
public class NlpAnalysisResult
{
    public string Engine { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public List<string> Words { get; set; } = [];
    public int TotalWordCount { get; set; }
    public int StopWordCount { get; set; }
    public List<string> PersonalPronouns { get; set; } = [];
    public List<string> PersonNames { get; set; } = [];
    public List<string> RootNouns { get; set; } = [];
    public List<string> RootVerbs { get; set; } = [];
    public int NounCount { get; set; }
    public int VerbCount { get; set; }
    public Dictionary<string, List<NlpEntity>> Entities { get; set; } = [];
}

public class NlpEntity
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("canonical")]
    public string Canonical { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}
