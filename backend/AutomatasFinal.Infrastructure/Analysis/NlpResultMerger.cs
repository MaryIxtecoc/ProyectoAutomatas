using System.Text.Json;
using AutomatasFinal.Domain.Entities;

namespace AutomatasFinal.Infrastructure.Analysis;

/// <summary>
/// Combina el resultado del analizador propio (TextAnalyzer) con el del
/// servicio NLP (spaCy/Stanza), sin cambiar la forma de los datos que se
/// guardan en la base ni la que lee el frontend.
///
/// Reglas:
/// - Conteo de palabras y frecuencias: se quedan los del analizador propio
///   (ya eran correctos), salvo en chino, donde el NLP segmenta mejor.
/// - Pronombres: los del NLP, que mira la categoría real de cada palabra
///   (así "sí" afirmativo ya no cuenta como pronombre). Solo si el NLP no
///   devolvió palabras se conservan los del analizador propio.
/// - Sustantivos y verbos en forma raíz: los del NLP (usan categoría
///   gramatical real y lema, en vez de listas fijas y sufijos).
/// - Entidades: manda el diccionario de la base de datos (si dice que
///   "Guatemala" es país, se respeta); el NLP agrega las que falten.
/// - Personas: solo las entidades de tipo PERSON (ya no entran países,
///   empresas ni palabras con mayúscula inicial como "Casa"), y se unen las
///   variantes de un mismo nombre ("María" + "María López" = "María López").
/// </summary>
public static class NlpResultMerger
{
    public const string ManualEngine = "manual";

    public static AnalysisOutput Merge(
        AnalysisOutput manual,
        NlpAnalysisResult nlp,
        AnalysisLanguage language)
    {
        var other = manual.OtherClassifications;

        // ---- Entidades -------------------------------------------------
        var entities = ReadEntities(other);
        var known = new HashSet<string>(
            entities.Values.SelectMany(list => list.Select(e => e.Canonical)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (type, list) in nlp.Entities)
        {
            foreach (var entity in list)
            {
                var canonical = string.IsNullOrWhiteSpace(entity.Canonical)
                    ? entity.Text
                    : entity.Canonical;

                if (string.IsNullOrWhiteSpace(canonical) || !known.Add(canonical))
                {
                    continue;
                }

                if (!entities.TryGetValue(type, out var bucket))
                {
                    bucket = [];
                    entities[type] = bucket;
                }

                bucket.Add(new NlpEntity
                {
                    Text = entity.Text,
                    Canonical = canonical,
                    Language = entity.Language ?? language.ToString()
                });
            }
        }

        var personNames = new List<string>();

        if (entities.TryGetValue("PERSON", out var persons))
        {
            personNames = UnifyPersonNames(persons.Select(p => p.Canonical));

            var keep = new HashSet<string>(personNames, StringComparer.OrdinalIgnoreCase);
            entities["PERSON"] = persons.Where(p => keep.Contains(p.Canonical)).ToList();
        }

        var nameWords = new HashSet<string>(
            personNames.SelectMany(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.OrdinalIgnoreCase);

        // ---- Pronombres, sustantivos y verbos ---------------------------
        var pronounSource = nlp.Words.Count > 0
            ? nlp.PersonalPronouns
            : manual.PersonalPronouns.Concat(nlp.PersonalPronouns);

        var pronouns = pronounSource
            .Select(x => x.ToLowerInvariant())
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var nouns = nlp.RootNouns
            .Where(x => !nameWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var verbs = nlp.RootVerbs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        manual.PersonalPronouns = pronouns;
        manual.PersonNames = personNames;
        manual.RootNouns = nouns;
        manual.RootVerbs = verbs;

        // ---- Chino: conteos con la segmentación del NLP -----------------
        if (language == AnalysisLanguage.ZH && nlp.Words.Count > 0)
        {
            var frequencies = nlp.Words
                .GroupBy(x => x)
                .Select(g => new WordFrequency { Word = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Word)
                .ToList();

            manual.TotalWordCount = nlp.Words.Count;
            manual.MostFrequentWords = frequencies.Take(10).ToList();
            manual.LeastFrequentWords = frequencies
                .OrderBy(x => x.Count)
                .ThenBy(x => x.Word)
                .Take(10)
                .ToList();

            other["uniqueWordCount"] = frequencies.Count;
            other["stopWordCount"] = nlp.StopWordCount;
            other["contentWordCount"] = nlp.Words.Count - nlp.StopWordCount;
            other["lexicalDiversity"] = Math.Round((double)frequencies.Count / nlp.Words.Count, 4);
            other["averageWordLength"] = Math.Round(nlp.Words.Average(x => x.Length), 2);
        }

        other["verbCount"] = nlp.VerbCount;
        other["nounCount"] = nlp.NounCount;
        other["pronounCount"] = pronouns.Count;
        other["personNameCount"] = personNames.Count;
        other["entities"] = entities;
        other["analysisEngine"] = string.IsNullOrWhiteSpace(nlp.Model)
            ? nlp.Engine
            : $"{nlp.Engine}:{nlp.Model}";

        return manual;
    }

    /// <summary>
    /// Une variantes del mismo nombre: si "María" aparece como palabra dentro
    /// de "María López", se deja solo el nombre completo.
    /// </summary>
    public static List<string> UnifyPersonNames(IEnumerable<string> names)
    {
        var distinct = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct
            .Where(name =>
            {
                var isSingleWord = !name.Contains(' ');
                var partOfLonger = distinct.Any(other =>
                    !string.Equals(other, name, StringComparison.OrdinalIgnoreCase) &&
                    other.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains(name, StringComparer.OrdinalIgnoreCase));

                return !(isSingleWord && partOfLonger);
            })
            .OrderBy(x => x)
            .ToList();
    }

    public static void MarkManual(AnalysisOutput manual)
    {
        manual.OtherClassifications["analysisEngine"] = ManualEngine;
    }

    /// <summary>
    /// Las entidades del analizador propio vienen como objetos anónimos
    /// { text, canonical, language }; se pasan por JSON para leerlas tipadas.
    /// </summary>
    private static Dictionary<string, List<NlpEntity>> ReadEntities(
        Dictionary<string, object> other)
    {
        if (!other.TryGetValue("entities", out var raw) || raw is null)
        {
            return new Dictionary<string, List<NlpEntity>>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = JsonSerializer.Serialize(raw);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<NlpEntity>>>(json);

            return parsed is null
                ? new Dictionary<string, List<NlpEntity>>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<NlpEntity>>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, List<NlpEntity>>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
