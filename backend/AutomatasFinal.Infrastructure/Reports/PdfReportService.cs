using System.Text.Json;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Storage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AutomatasFinal.Infrastructure.Reports;

public class PdfReportService
{
    private readonly SupabaseStorageService _storage;

    public PdfReportService(SupabaseStorageService storage)
    {
        _storage = storage;
    }

    public async Task<string> GenerateAsync(
        AnalysisJob job,
        AnalysisResult result)
    {
        var mostFrequentWords =
            DeserializeList(result.MostFrequentWords);

        var leastFrequentWords =
            DeserializeList(result.LeastFrequentWords);

        var personalPronouns =
            DeserializeList(result.PersonalPronouns);

        var personNames =
            DeserializeList(result.PersonNames);

        var rootNouns =
            DeserializeList(result.RootNouns);

        var rootVerbs =
            DeserializeList(result.RootVerbs);

        var metrics =
            DeserializeMetrics(result.OtherClassifications);

        var entities =
            DeserializeEntities(result.OtherClassifications);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);

                // Lato no tiene caracteres chinos ni árabes: se agregan
                // fuentes de respaldo (ver configuración en Program.cs).
                page.DefaultTextStyle(
                    x => x.FontSize(10).FontFamily(
                        "Lato",
                        "Microsoft YaHei",
                        "SimSun",
                        "Segoe UI",
                        "Arial",
                        "Noto Sans CJK SC",
                        "Noto Sans SC",
                        "Noto Sans Arabic",
                        "Noto Naskh Arabic",
                        "DejaVu Sans"));

                page.Header().Column(header =>
                {
                    header.Item()
                        .Text("REPORTE DE ANÁLISIS LINGÜÍSTICO")
                        .Bold()
                        .FontSize(18);

                    header.Item()
                        .PaddingTop(5)
                        .Text($"Archivo: {job.FileName}")
                        .FontSize(10);

                    header.Item()
                        .Text($"Idioma: {job.Language}")
                        .FontSize(10);
                });

                page.Content()
                    .PaddingVertical(20)
                    .Column(column =>
                    {
                        column.Spacing(15);

                        AddSection(
                            column,
                            "Resumen",
                            () =>
                            {
                                AddMetric(
                                    column,
                                    "Total de palabras",
                                    result.TotalWordCount.ToString());

                                AddMetric(
                                    column,
                                    "Palabras únicas",
                                    metrics.UniqueWordCount.ToString());

                                AddMetric(
                                    column,
                                    "Diversidad léxica",
                                    metrics.LexicalDiversity
                                        .ToString("0.####"));

                                AddMetric(
                                    column,
                                    "Longitud promedio",
                                    metrics.AverageWordLength
                                        .ToString("0.##"));
                            });

                        AddListSection(
                            column,
                            "Palabras más frecuentes",
                            mostFrequentWords);

                        AddListSection(
                            column,
                            "Palabras menos frecuentes",
                            leastFrequentWords);

                        AddListSection(
                            column,
                            "Pronombres personales",
                            personalPronouns);

                        AddListSection(
                            column,
                            "Nombres de personas",
                            personNames);

                        AddListSection(
                            column,
                            "Sustantivos raíz",
                            rootNouns);

                        AddListSection(
                            column,
                            "Verbos raíz",
                            rootVerbs);

                        AddEntitySection(
                            column,
                            "Entidades detectadas",
                            entities);
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span(
                            "AutomatasFinal - Reporte generado automáticamente");
                    });
            });
        });

        await using var pdfStream = new MemoryStream();

        document.GeneratePdf(pdfStream);

        pdfStream.Position = 0;

        var reportPath =
            $"{job.UserId}/{job.Id}.pdf";

        await _storage.UploadAsync(
            "analysis-reports",
            reportPath,
            pdfStream,
            "application/pdf");

        return reportPath;
    }

    private static void AddEntitySection(
        ColumnDescriptor column,
        string title,
        Dictionary<string, List<EntityItem>> entities)
    {
        if (entities.Count == 0)
        {
            AddListSection(
                column,
                title,
                new List<string> { "No se detectaron entidades." });

            return;
        }

        column.Item()
            .Text(title)
            .Bold()
            .FontSize(14);

        foreach (var group in entities.OrderBy(x => x.Key))
        {
            column.Item()
                .Text(GetEntityTypeLabel(group.Key))
                .Bold()
                .FontSize(11);

            foreach (var entity in group.Value)
            {
                column.Item()
                    .PaddingLeft(10)
                    .Text($"• {entity.Text}");
            }
        }
    }

    private static string GetEntityTypeLabel(
        string entityType)
    {
        return entityType.ToUpperInvariant() switch
        {
            "PERSON" => "Personas",
            "COUNTRY" => "Países",
            "CITY" => "Ciudades",
            "ORGANIZATION" => "Organizaciones",
            "BRAND" => "Marcas",
            "PRODUCT" => "Productos",
            "LOCATION" => "Ubicaciones",
            _ => entityType
        };
    }

    private static Dictionary<string, List<EntityItem>>
        DeserializeEntities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            using var document =
                JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty(
                    "entities",
                    out var entitiesElement))
            {
                return new();
            }

            var result =
                new Dictionary<string, List<EntityItem>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var entityType in entitiesElement.EnumerateObject())
            {
                var items = new List<EntityItem>();

                foreach (var item in entityType.Value.EnumerateArray())
                {
                    var text =
                        item.TryGetProperty("text", out var textElement)
                            ? textElement.GetString()
                            : null;

                    var canonical =
                        item.TryGetProperty("canonical", out var canonicalElement)
                            ? canonicalElement.GetString()
                            : null;

                    var language =
                        item.TryGetProperty("language", out var languageElement)
                            ? languageElement.GetString()
                            : null;

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        items.Add(new EntityItem
                        {
                            Text = text,
                            Canonical = canonical,
                            Language = language
                        });
                    }
                }

                if (items.Count > 0)
                {
                    result[entityType.Name] = items;
                }
            }

            return result;
        }
        catch
        {
            return new();
        }
    }

    private static List<string> DeserializeList(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)
                   ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static List<WordFrequency> DeserializeWordFrequencies(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            return JsonSerializer.Deserialize<List<WordFrequency>>(
                       json)
                   ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static List<string> DeserializeListFromWords(
        string? json)
    {
        var words = DeserializeWordFrequencies(json);

        return words
            .Select(x => x.Word)
            .ToList();
    }

    private static AnalysisMetrics DeserializeMetrics(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            using var document =
                JsonDocument.Parse(json);

            var root = document.RootElement;

            return new AnalysisMetrics
            {
                UniqueWordCount =
                    GetInt(root, "uniqueWordCount"),

                LexicalDiversity =
                    GetDouble(root, "lexicalDiversity"),

                AverageWordLength =
                    GetDouble(root, "averageWordLength")
            };
        }
        catch
        {
            return new();
        }
    }

    private static int GetInt(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(
                   property,
                   out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static double GetDouble(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(
                   property,
                   out var value) &&
               value.TryGetDouble(out var result)
            ? result
            : 0;
    }

    private static void AddSection(
        ColumnDescriptor column,
        string title,
        Action content)
    {
        column.Item()
            .Text(title)
            .Bold()
            .FontSize(14);

        content();
    }

    private static void AddMetric(
        ColumnDescriptor column,
        string label,
        string value)
    {
        column.Item()
            .Text($"{label}: {value}");
    }

    private static void AddListSection(
        ColumnDescriptor column,
        string title,
        List<string> items)
    {
        column.Item()
            .Text(title)
            .Bold()
            .FontSize(14);

        if (items.Count == 0)
        {
            column.Item()
                .Text("No se encontraron datos.");

            return;
        }

        foreach (var item in items)
        {
            column.Item()
                .PaddingLeft(10)
                .Text($"• {item}");
        }
    }

    private class EntityItem
    {
        public string Text { get; set; } = string.Empty;

        public string? Canonical { get; set; }

        public string? Language { get; set; }
    }

    private class AnalysisMetrics
    {
        public int UniqueWordCount { get; set; }

        public double LexicalDiversity { get; set; }

        public double AverageWordLength { get; set; }
    }

    private class WordFrequency
    {
        public string Word { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}