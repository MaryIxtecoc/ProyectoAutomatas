using System.Text.RegularExpressions;
using AutomatasFinal.Domain.Entities;

namespace AutomatasFinal.Infrastructure.Analysis;

public class TextAnalyzer
{
    public AnalysisOutput Analyze(
        string text,
        AnalysisLanguage language,
        IEnumerable<EntityDictionaryEntry>? dictionary = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new AnalysisOutput();

        var profile = LanguageProfiles.Get(language);

        var rawTokens = language == AnalysisLanguage.ZH
            ? TokenizeChinese(text, profile.AllKnownWords)
            : Tokenize(text);

        var words = rawTokens
            .Select(x => x.ToLowerInvariant())
            .ToList();

        var frequencies = words
            .GroupBy(x => x)
            .Select(g => new WordFrequency
            {
                Word = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Word)
            .ToList();

        var mostFrequent = frequencies
            .Take(10)
            .ToList();

        var leastFrequent = frequencies
            .OrderBy(x => x.Count)
            .ThenBy(x => x.Word)
            .Take(10)
            .ToList();

        var pronouns = DetectPronouns(words, profile);

        var entities = DetectEntities(
            rawTokens,
            dictionary ?? Enumerable.Empty<EntityDictionaryEntry>());

        var personNames = entities
            .Where(x => x.EntityType == "PERSON")
            .Select(x => x.CanonicalValue ?? x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        // El diccionario sembrado en BD es finito (150 nombres); esto
        // complementa con nombres que NO están ahí, usando la mayúscula
        // inicial como pista (no aplica a AR/ZH, ver UsesCapitalizationForNames).
        var alreadyClassified = new HashSet<string>(
            personNames.SelectMany(n =>
                n.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.OrdinalIgnoreCase);

        var heuristicNames = DetectCapitalizedNameCandidates(text, profile, alreadyClassified);

        personNames = personNames
            .Concat(heuristicNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var verbs = DetectVerbs(words, personNames, profile);

        var nouns = DetectNouns(words, verbs, personNames, profile);

        var rootVerbs = verbs
            .Select(word => LemmatizeVerb(word, profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var rootNouns = nouns
            .Select(word => LemmatizeNoun(word, profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var stopWordCount = words.Count(profile.Stopwords.Contains);

        var contentWords = words
            .Where(x => !profile.Stopwords.Contains(x))
            .ToList();

        var entitiesByType = entities
            .GroupBy(x => x.EntityType)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(x => new
                    {
                        text = x.Value,
                        canonical = x.CanonicalValue ?? x.Value,
                        language = x.Language
                    })
                    .Distinct()
                    .ToList());

        var otherClassifications =
            new Dictionary<string, object>
            {
                ["language"] = language.ToString(),
                ["uniqueWordCount"] = frequencies.Count,
                ["stopWordCount"] = stopWordCount,
                ["contentWordCount"] = contentWords.Count,

                ["lexicalDiversity"] = words.Count == 0
                    ? 0
                    : Math.Round(
                        (double)frequencies.Count / words.Count,
                        4),

                ["averageWordLength"] = words.Count == 0
                    ? 0
                    : Math.Round(
                        words.Average(x => x.Length),
                        2),

                ["verbCount"] = verbs.Count,
                ["nounCount"] = nouns.Count,
                ["pronounCount"] = pronouns.Count,
                ["personNameCount"] = personNames.Count,

                ["entities"] = entitiesByType
            };

        return new AnalysisOutput
        {
            TotalWordCount = words.Count,
            MostFrequentWords = mostFrequent,
            LeastFrequentWords = leastFrequent,
            PersonalPronouns = pronouns,
            PersonNames = personNames,
            RootNouns = rootNouns,
            RootVerbs = rootVerbs,
            OtherClassifications = otherClassifications
        };
    }

    // --- Tokenización ---

    private static List<string> Tokenize(string text)
    {
        // Sirve para idiomas que separan palabras con espacios/signos y usan
        // escritura alfabética o abjad (ES, EN, RU, AR). \p{L}\p{M} cubre
        // letras y diacríticos de cualquier alfabeto, incluyendo árabe y cirílico.
        return Regex
            .Matches(
                text,
                @"[\p{L}\p{M}]+(?:['’-][\p{L}\p{M}]+)*")
            .Select(x => x.Value)
            .Where(x => x.Length > 0)
            .ToList();
    }

    /// <summary>
    /// El chino no separa palabras con espacios, así que no se puede tokenizar
    /// con el mismo regex. Aquí se usa "maximum matching": dentro de cada
    /// tramo de caracteres Han consecutivos, se intenta calzar primero la
    /// palabra conocida más larga (2 a 4 caracteres) contra el diccionario
    /// del idioma; si no hay calce, se toma un solo carácter como token.
    /// No es un segmentador real (eso requeriría un diccionario mucho más
    /// grande o un modelo estadístico), pero da resultados mucho más útiles
    /// que tratar cada carácter como palabra suelta.
    /// </summary>
    private static List<string> TokenizeChinese(
        string text,
        HashSet<string> knownWords)
    {
        var tokens = new List<string>();
        var run = new List<char>();

        void FlushRun()
        {
            if (run.Count == 0) return;

            var runText = new string(run.ToArray());
            var i = 0;

            while (i < runText.Length)
            {
                var matched = false;
                var maxLen = Math.Min(4, runText.Length - i);

                for (var len = maxLen; len >= 2; len--)
                {
                    var candidate = runText.Substring(i, len);

                    if (knownWords.Contains(candidate))
                    {
                        tokens.Add(candidate);
                        i += len;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    tokens.Add(runText[i].ToString());
                    i += 1;
                }
            }

            run.Clear();
        }

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                run.Add(ch);
            }
            else
            {
                FlushRun();
            }
        }

        FlushRun();

        return tokens;
    }

    // --- Detección (parametrizada por perfil de idioma) ---

    /// <summary>
    /// Detecta candidatos a nombre de persona por mayúscula inicial, para
    /// idiomas que la usan (ver LanguageProfile.UsesCapitalizationForNames).
    /// No usa el diccionario: complementa los nombres que el diccionario
    /// (finito, 150 nombres) no cubre. Reglas: la palabra empieza con
    /// mayúscula, no está en mayúsculas completas (para no marcar siglas),
    /// no es una palabra ya clasificada (stopword/pronombre/nombre de
    /// diccionario) y no es la primera palabra de una oración (para no
    /// marcar cualquier palabra que abre oración).
    /// </summary>
    private static List<string> DetectCapitalizedNameCandidates(
        string text,
        LanguageProfile profile,
        HashSet<string> alreadyClassified)
    {
        if (!profile.UsesCapitalizationForNames || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var candidates = new List<string>();

        var matches = Regex.Matches(text, @"[\p{L}\p{M}]+(?:['’-][\p{L}\p{M}]+)*");

        // Palabras que en el texto también aparecen escritas en minúscula
        // ("casa ... Casa"): son palabras comunes, no nombres propios.
        var lowercaseWords = new HashSet<string>(
            matches.Select(m => m.Value).Where(w => w.Length > 0 && char.IsLower(w[0])),
            StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            var word = match.Value;

            if (word.Length < 2 || !char.IsUpper(word[0]))
            {
                continue;
            }

            if (lowercaseWords.Contains(word.ToLowerInvariant()))
            {
                continue;
            }

            if (word == word.ToUpperInvariant())
            {
                // Todo en mayúsculas: más probable que sea una sigla que un nombre.
                continue;
            }

            var lower = word.ToLowerInvariant();

            if (alreadyClassified.Contains(lower) ||
                profile.Stopwords.Contains(lower) ||
                profile.Pronouns.Contains(lower) ||
                profile.KnownVerbs.Contains(lower) ||
                profile.KnownNouns.Contains(lower))
            {
                continue;
            }

            var i = match.Index - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i]))
            {
                i--;
            }

            // i < 0: es la primera palabra del texto -> también cuenta como
            // inicio de oración y se descarta.
            var isSentenceStart = i < 0 || text[i] is '.' or '!' or '?' or '\n' or ':' or '¡' or '¿';

            if (isSentenceStart)
            {
                continue;
            }

            candidates.Add(word);
        }

        return candidates;
    }

    private static List<string> DetectPronouns(
        IEnumerable<string> words,
        LanguageProfile profile)
    {
        return words
            .Where(profile.Pronouns.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static List<DetectedEntity> DetectEntities(
        IEnumerable<string> tokens,
        IEnumerable<EntityDictionaryEntry> dictionary)
    {
        var dictionaryList = dictionary.ToList();

        var result = new List<DetectedEntity>();

        foreach (var token in tokens)
        {
            var normalizedToken = token.ToLowerInvariant();

            var matches = dictionaryList
                .Where(x =>
                    string.Equals(
                        x.NormalizedValue,
                        normalizedToken,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var match in matches)
            {
                result.Add(new DetectedEntity
                {
                    Value = token,
                    CanonicalValue = match.CanonicalValue ?? match.Value,
                    EntityType = match.EntityType,
                    Language = match.Language
                });
            }
        }

        return result
            .GroupBy(x =>
                $"{x.Value}|{x.EntityType}",
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static List<string> DetectVerbs(
        IEnumerable<string> words,
        IEnumerable<string> personNames,
        LanguageProfile profile)
    {
        var names = new HashSet<string>(
            personNames.SelectMany(x =>
                x.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.OrdinalIgnoreCase);

        return words
            .Where(word => !names.Contains(word))
            .Where(word => IsLikelyVerb(word, profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static bool IsLikelyVerb(string word, LanguageProfile profile)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        if (profile.NonVerbs.Contains(word))
            return false;

        if (profile.KnownVerbs.Contains(word))
            return true;

        if (word.Length < profile.MinVerbHeuristicLength)
            return false;

        return profile.VerbSuffixes.Any(suffix =>
            word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> DetectNouns(
        IEnumerable<string> words,
        IEnumerable<string> verbs,
        IEnumerable<string> personNames,
        LanguageProfile profile)
    {
        var verbSet = new HashSet<string>(verbs, StringComparer.OrdinalIgnoreCase);

        var nameSet = new HashSet<string>(
            personNames.SelectMany(x =>
                x.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.OrdinalIgnoreCase);

        return words
            .Where(word => !verbSet.Contains(word))
            .Where(word => !nameSet.Contains(word))
            .Where(word => IsLikelyNoun(word, profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static bool IsLikelyNoun(string word, LanguageProfile profile)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        if (profile.Stopwords.Contains(word))
            return false;

        if (IsLikelyVerb(word, profile))
            return false;

        if (profile.KnownNouns.Contains(word))
            return true;

        if (word.Length < profile.MinNounHeuristicLength)
            return false;

        return profile.NounSuffixes.Any(suffix =>
            word.Length > suffix.Length + 2 &&
            word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    // --- Lematización (forma raíz) ---

    private static string LemmatizeVerb(string word, LanguageProfile profile)
    {
        var lower = word.ToLowerInvariant();

        if (profile.IrregularVerbLemmas.TryGetValue(lower, out var lemma))
            return lemma;

        // En español y ruso los sufijos verbales de la heurística son de
        // infinitivo: la palabra YA es la forma raíz ("analizar"), no se recorta.
        if (profile.VerbSuffixesAreInfinitives)
            return lower;

        foreach (var suffix in profile.VerbSuffixes.OrderByDescending(s => s.Length))
        {
            if (lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                lower.Length > suffix.Length + 2)
            {
                return lower[..^suffix.Length] + profile.VerbInfinitiveHint;
            }
        }

        return lower;
    }

    private static string LemmatizeNoun(string word, LanguageProfile profile)
    {
        var lower = word.ToLowerInvariant();

        if (profile.IrregularNounLemmas.TryGetValue(lower, out var lemma))
            return lemma;

        foreach (var suffix in profile.PluralSuffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                lower.Length > suffix.Length + 2)
            {
                return lower[..^suffix.Length];
            }
        }

        return lower;
    }
}

/// <summary>
/// Recursos lingüísticos de un idioma: pronombres, palabras vacías
/// (stopwords), listas de verbos/sustantivos conocidos del dominio del
/// proyecto (programación, análisis, estudiantes...) y las heurísticas de
/// sufijos usadas cuando la palabra no está en las listas conocidas.
/// No es un analizador morfológico real -- es deliberadamente simple, igual
/// que el analizador original en español sobre el que está basado -- pero
/// ya no ignora los otros 4 idiomas: cada uno tiene sus propias listas.
/// </summary>
internal class LanguageProfile
{
    public required HashSet<string> Pronouns { get; init; }
    public required HashSet<string> Stopwords { get; init; }
    public required HashSet<string> KnownVerbs { get; init; }
    public required HashSet<string> KnownNouns { get; init; }
    public required string[] VerbSuffixes { get; init; }
    public required string[] NounSuffixes { get; init; }
    public required string[] PluralSuffixes { get; init; }
    public required Dictionary<string, string> IrregularVerbLemmas { get; init; }
    public required Dictionary<string, string> IrregularNounLemmas { get; init; }
    public int MinVerbHeuristicLength { get; init; } = 3;
    public int MinNounHeuristicLength { get; init; } = 4;

    /// <summary>
    /// ES/EN/RU escriben los nombres propios con mayúscula inicial, así que
    /// una palabra capitalizada que no empieza oración y no es una palabra
    /// conocida es candidata a nombre de persona -- esto detecta nombres
    /// que NO están en el diccionario sembrado en la base de datos (que por
    /// definición es finito). AR y ZH no tienen mayúsculas, así que ahí
    /// esta heurística queda apagada y la detección de nombres depende
    /// solo del diccionario.
    /// </summary>
    public bool UsesCapitalizationForNames { get; init; } = true;

    /// <summary>Sufijo que se reagrega al recortar un verbo regular por heurística
    /// (p. ej. en español, al quitar una terminación conjugada, se vuelve a
    /// dejar en infinitivo agregando "ar"/"er"/"ir" según el caso -- aquí se
    /// simplifica dejándolo vacío salvo que el idioma lo necesite).</summary>
    public string VerbInfinitiveHint { get; init; } = string.Empty;

    /// <summary>true si VerbSuffixes son terminaciones de infinitivo (ES, RU).</summary>
    public bool VerbSuffixesAreInfinitives { get; init; }

    /// <summary>
    /// Palabras que terminan como un infinitivo pero NO son verbos
    /// ("ayer", "mujer", "lugar"). Evita falsos verbos en la heurística.
    /// </summary>
    public HashSet<string> NonVerbs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string>? _allKnownWords;

    /// <summary>Únicamente usado por el tokenizador de chino (maximum matching).</summary>
    public HashSet<string> AllKnownWords => _allKnownWords ??= new HashSet<string>(
        Pronouns.Concat(Stopwords).Concat(KnownVerbs).Concat(KnownNouns),
        StringComparer.OrdinalIgnoreCase);
}

internal static class LanguageProfiles
{
    private static readonly Dictionary<AnalysisLanguage, LanguageProfile> Profiles = new()
    {
        [AnalysisLanguage.ES] = BuildSpanish(),
        [AnalysisLanguage.EN] = BuildEnglish(),
        [AnalysisLanguage.RU] = BuildRussian(),
        [AnalysisLanguage.ZH] = BuildChinese(),
        [AnalysisLanguage.AR] = BuildArabic()
    };

    public static LanguageProfile Get(AnalysisLanguage language) =>
        Profiles.TryGetValue(language, out var profile)
            ? profile
            : Profiles[AnalysisLanguage.ES];

    private static LanguageProfile BuildSpanish() => new()
    {
        Pronouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "yo", "tú", "tu", "él", "ella", "usted",
            "nosotros", "nosotras", "vosotros", "vosotras",
            "ustedes", "ellos", "ellas",
            "me", "te", "se", "nos", "os", "le", "les",
            "mí", "ti", "sí", "conmigo", "contigo", "consigo"
        },
        Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "al", "ante", "bajo", "con", "contra", "de", "del",
            "desde", "durante", "en", "entre", "hacia", "hasta",
            "para", "por", "según", "sin", "sobre", "tras",
            "el", "la", "los", "las", "un", "una", "unos", "unas",
            "y", "e", "o", "u", "ni", "pero", "aunque", "porque",
            "que", "como", "si", "cuando", "donde",
            "muy", "más", "menos", "ya", "también", "solo", "sólo",
            "yo", "tú", "tu", "él", "ella", "usted",
            "nosotros", "nosotras", "vosotros", "vosotras",
            "ustedes", "ellos", "ellas",
            "me", "te", "se", "nos", "os", "lo", "le", "les"
        },
        KnownVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "ser", "estar", "haber", "tener",
            "soy", "eres", "es", "somos", "son",
            "era", "eras", "éramos", "eran",
            "fui", "fuiste", "fue", "fuimos", "fueron",
            "estoy", "estás", "está", "estamos", "están",
            "estaba", "estaban",
            "he", "has", "ha", "hemos", "han",
            "había", "habían",
            "tengo", "tienes", "tiene", "tenemos", "tienen",
            "tenía", "tenían",
            "estudiar", "estudia", "estudian", "estudiamos",
            "estudié", "estudiaste", "estudió",
            "desarrollar", "desarrolla", "desarrollan", "desarrollamos",
            "desarrollé", "desarrollaste", "desarrolló",
            "programar", "programa", "programan", "programamos",
            "programé", "programaste", "programó",
            "analizar", "analiza", "analizan", "analizamos",
            "analicé", "analizaste", "analizó",
            "construir", "construye", "construyen", "construimos",
            "construí", "construiste", "construyó",
            "aprender", "aprende", "aprendo", "aprenden",
            "aprendemos", "aprendí", "aprendiste", "aprendió",
            "querer", "quiere", "quiero", "quieren",
            "queremos", "quise", "quisiste", "quiso"
        },
        KnownNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "programación", "sistema", "sistemas", "ingeniería",
            "aplicación", "aplicaciones", "estudiante", "estudiantes",
            "texto", "textos", "proyecto", "proyectos", "programa",
            "programas", "software", "computadora", "computadoras",
            "lenguaje", "lenguajes", "análisis", "algoritmo",
            "algoritmos", "base", "datos", "usuario", "usuarios",
            "archivo", "archivos", "resultado", "resultados",
            "informe", "informes"
        },
        VerbSuffixes = ["ar", "er", "ir"],
        VerbSuffixesAreInfinitives = true,
        NonVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "ayer", "anteayer", "mujer", "lugar", "hogar", "mar", "altar",
            "azar", "bar", "collar", "dólar", "hangar", "militar", "particular",
            "popular", "regular", "similar", "familiar", "escolar", "solar",
            "titular", "nectar", "néctar", "alfiler", "carácter", "cráter",
            "taller", "cáncer", "líder", "póster", "suéter", "elixir", "faquir",
            "nadir", "tapir", "zafir", "por", "superior", "inferior", "mayor",
            "menor", "mejor", "peor", "anterior", "posterior", "interior",
            "exterior", "señor", "amor", "color", "dolor", "olor"
        },
        NounSuffixes =
        [
            "ción", "sión", "dad", "tad", "tud", "miento", "mento",
            "ancia", "encia", "ismo", "ura", "eza", "aje"
        ],
        PluralSuffixes = ["es", "os", "as", "s"],
        IrregularVerbLemmas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["soy"] = "ser", ["eres"] = "ser", ["es"] = "ser", ["somos"] = "ser", ["son"] = "ser",
            ["era"] = "ser", ["eras"] = "ser", ["éramos"] = "ser", ["eran"] = "ser",
            ["fui"] = "ser", ["fuiste"] = "ser", ["fue"] = "ser", ["fuimos"] = "ser", ["fueron"] = "ser",
            ["estoy"] = "estar", ["estás"] = "estar", ["está"] = "estar", ["estamos"] = "estar",
            ["están"] = "estar", ["estaba"] = "estar", ["estaban"] = "estar",
            ["he"] = "haber", ["has"] = "haber", ["ha"] = "haber", ["hemos"] = "haber", ["han"] = "haber",
            ["había"] = "haber", ["habían"] = "haber",
            ["tengo"] = "tener", ["tienes"] = "tener", ["tiene"] = "tener", ["tenemos"] = "tener",
            ["tienen"] = "tener", ["tenía"] = "tener", ["tenían"] = "tener",
            ["estudia"] = "estudiar", ["estudian"] = "estudiar", ["estudiamos"] = "estudiar",
            ["estudié"] = "estudiar", ["estudiaste"] = "estudiar", ["estudió"] = "estudiar",
            ["desarrolla"] = "desarrollar", ["desarrollan"] = "desarrollar", ["desarrollamos"] = "desarrollar",
            ["desarrollé"] = "desarrollar", ["desarrollaste"] = "desarrollar", ["desarrolló"] = "desarrollar",
            ["programa"] = "programar", ["programan"] = "programar", ["programamos"] = "programar",
            ["programé"] = "programar", ["programaste"] = "programar", ["programó"] = "programar",
            ["analiza"] = "analizar", ["analizan"] = "analizar", ["analizamos"] = "analizar",
            ["analicé"] = "analizar", ["analizaste"] = "analizar", ["analizó"] = "analizar",
            ["construye"] = "construir", ["construyen"] = "construir", ["construimos"] = "construir",
            ["construí"] = "construir", ["construiste"] = "construir", ["construyó"] = "construir",
            ["aprende"] = "aprender", ["aprendo"] = "aprender", ["aprenden"] = "aprender",
            ["aprendemos"] = "aprender", ["aprendí"] = "aprender", ["aprendiste"] = "aprender", ["aprendió"] = "aprender",
            ["quiere"] = "querer", ["quiero"] = "querer", ["quieren"] = "querer",
            ["queremos"] = "querer", ["quise"] = "querer", ["quisiste"] = "querer", ["quiso"] = "querer"
        },
        IrregularNounLemmas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sistemas"] = "sistema", ["proyectos"] = "proyecto", ["textos"] = "texto",
            ["estudiantes"] = "estudiante", ["aplicaciones"] = "aplicación", ["datos"] = "dato",
            ["usuarios"] = "usuario", ["archivos"] = "archivo", ["resultados"] = "resultado",
            ["informes"] = "informe", ["programas"] = "programa", ["computadoras"] = "computadora",
            ["lenguajes"] = "lenguaje", ["algoritmos"] = "algoritmo"
        }
    };

    private static LanguageProfile BuildEnglish() => new()
    {
        Pronouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "i", "you", "he", "she", "it", "we", "they",
            "me", "him", "her", "us", "them",
            "my", "your", "his", "its", "our", "their",
            "mine", "yours", "hers", "ours", "theirs",
            "myself", "yourself", "himself", "herself",
            "itself", "ourselves", "yourselves", "themselves"
        },
        Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "and", "or", "but", "if", "then", "so",
            "because", "as", "until", "while", "of", "at", "by", "for",
            "with", "about", "against", "between", "into", "through",
            "during", "before", "after", "above", "below", "to", "from",
            "up", "down", "in", "out", "on", "off", "over", "under",
            "again", "further", "once", "here", "there", "when", "where",
            "why", "how", "all", "any", "both", "each", "few", "more",
            "most", "other", "some", "such", "no", "nor", "not", "only",
            "own", "same", "than", "too", "very", "is", "are", "was",
            "were", "be", "been", "being", "have", "has", "had", "having",
            "do", "does", "did", "doing", "this", "that", "these", "those",
            "i", "you", "he", "she", "it", "we", "they",
            "me", "him", "her", "us", "them",
            "my", "your", "his", "its", "our", "their"
        },
        KnownVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did",
            "will", "would", "can", "could", "shall", "should", "may", "might", "must",
            "study", "studies", "studied", "studying",
            "develop", "develops", "developed", "developing",
            "program", "programs", "programmed", "programming",
            "analyze", "analyzes", "analyzed", "analyzing",
            "build", "builds", "built", "building",
            "learn", "learns", "learned", "learning",
            "want", "wants", "wanted", "wanting",
            "create", "creates", "created", "creating",
            "use", "uses", "used", "using"
        },
        KnownNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "programming", "system", "systems", "engineering",
            "application", "applications", "student", "students",
            "text", "texts", "project", "projects", "program", "programs",
            "software", "computer", "computers", "language", "languages",
            "analysis", "algorithm", "algorithms", "database", "user",
            "users", "file", "files", "result", "results", "report", "reports"
        },
        VerbSuffixes = ["ing", "ed"],
        NounSuffixes =
        [
            "tion", "sion", "ness", "ment", "ity", "ance", "ence", "ism", "ship", "ology"
        ],
        PluralSuffixes = ["ies", "es", "s"],
        IrregularVerbLemmas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["is"] = "be", ["are"] = "be", ["was"] = "be", ["were"] = "be",
            ["been"] = "be", ["being"] = "be", ["am"] = "be",
            ["has"] = "have", ["had"] = "have", ["having"] = "have",
            ["does"] = "do", ["did"] = "do", ["doing"] = "do",
            ["studies"] = "study", ["studied"] = "study", ["studying"] = "study",
            ["develops"] = "develop", ["developed"] = "develop", ["developing"] = "develop",
            ["programs"] = "program", ["programmed"] = "program", ["programming"] = "program",
            ["analyzes"] = "analyze", ["analyzed"] = "analyze", ["analyzing"] = "analyze",
            ["builds"] = "build", ["built"] = "build", ["building"] = "build",
            ["learns"] = "learn", ["learned"] = "learn", ["learning"] = "learn",
            ["wants"] = "want", ["wanted"] = "want", ["wanting"] = "want",
            ["creates"] = "create", ["created"] = "create", ["creating"] = "create",
            ["uses"] = "use", ["used"] = "use", ["using"] = "use"
        },
        IrregularNounLemmas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["systems"] = "system", ["applications"] = "application",
            ["students"] = "student", ["projects"] = "project",
            ["programs"] = "program", ["languages"] = "language",
            ["algorithms"] = "algorithm", ["databases"] = "database",
            ["users"] = "user", ["files"] = "file", ["results"] = "result",
            ["reports"] = "report", ["computers"] = "computer", ["texts"] = "text"
        }
    };

    private static LanguageProfile BuildRussian() => new()
    {
        Pronouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "я", "ты", "он", "она", "оно", "мы", "вы", "они",
            "меня", "тебя", "его", "её", "нас", "вас", "их",
            "мне", "тебе", "ему", "ей", "нам", "вам", "им",
            "мной", "тобой", "нами", "вами", "ими",
            "себя", "свой", "своя", "своё", "свои"
        },
        Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "и", "в", "во", "не", "что", "он", "на", "я", "с", "со",
            "как", "а", "то", "все", "она", "так", "его", "но", "да",
            "ты", "к", "у", "же", "вы", "за", "бы", "по", "только",
            "её", "мне", "было", "вот", "от", "меня", "ещё", "нет",
            "о", "из", "ему", "теперь", "когда", "даже", "ну", "если",
            "уже", "или", "ни", "быть", "был", "до", "вас", "вам",
            "себя", "для", "мы", "тебя", "их", "чем", "была", "без",
            "чего", "тоже", "себе", "под", "будет", "тогда", "кто",
            "этот", "того", "этого", "какой", "здесь", "этом", "один",
            "мой", "тем", "чтобы", "нее", "куда", "можно", "при",
            "об", "другой", "после", "над", "тот", "через", "эти",
            "нас", "про", "них", "три", "эту", "моя", "перед", "том"
        },
        KnownVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "быть", "есть", "был", "была", "было", "были",
            "изучать", "изучает", "изучаю", "изучают", "изучали",
            "разрабатывать", "разрабатывает", "разрабатываю", "разрабатывают",
            "программировать", "программирует", "программирую", "программируют",
            "анализировать", "анализирует", "анализирую", "анализируют",
            "строить", "строит", "строю", "строят",
            "учиться", "учится", "учусь", "учатся",
            "хотеть", "хочет", "хочу", "хотят",
            "использовать", "использует", "использую", "используют"
        },
        KnownNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "программирование", "система", "системы", "инженерия",
            "приложение", "приложения", "студент", "студенты",
            "текст", "тексты", "проект", "проекты", "программа",
            "программы", "обеспечение", "компьютер", "компьютеры",
            "язык", "языки", "анализ", "алгоритм", "алгоритмы",
            "база", "данные", "пользователь", "пользователи",
            "файл", "файлы", "результат", "результаты",
            "отчёт", "отчеты", "отчёты"
        },
        VerbSuffixes = ["ать", "ять", "еть", "ить", "оть", "уть", "ти"],
        VerbSuffixesAreInfinitives = true,
        NounSuffixes = ["ание", "ение", "ция", "ность", "ство", "изм"],
        PluralSuffixes = ["ов", "ев", "ей", "ы", "и", "а", "я"],
        IrregularVerbLemmas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["есть"] = "быть", ["был"] = "быть", ["была"] = "быть",
            ["было"] = "быть", ["были"] = "быть",
            ["изучает"] = "изучать", ["изучаю"] = "изучать", ["изучают"] = "изучать", ["изучали"] = "изучать",
            ["разрабатывает"] = "разрабатывать", ["разрабатываю"] = "разрабатывать", ["разрабатывают"] = "разрабатывать",
            ["программирует"] = "программировать", ["программирую"] = "программировать", ["программируют"] = "программировать",
            ["анализирует"] = "анализировать", ["анализирую"] = "анализировать", ["анализируют"] = "анализировать",
            ["строит"] = "строить", ["строю"] = "строить", ["строят"] = "строить",
            ["учится"] = "учиться", ["учусь"] = "учиться", ["учатся"] = "учиться",
            ["хочет"] = "хотеть", ["хочу"] = "хотеть", ["хотят"] = "хотеть",
            ["использует"] = "использовать", ["использую"] = "использовать", ["используют"] = "использовать"
        },
        IrregularNounLemmas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["системы"] = "система", ["приложения"] = "приложение",
            ["студенты"] = "студент", ["тексты"] = "текст",
            ["проекты"] = "проект", ["программы"] = "программа",
            ["компьютеры"] = "компьютер", ["языки"] = "язык",
            ["алгоритмы"] = "алгоритм", ["пользователи"] = "пользователь",
            ["файлы"] = "файл", ["результаты"] = "результат",
            ["отчеты"] = "отчёт", ["отчёты"] = "отчёт"
        }
    };

    private static LanguageProfile BuildArabic() => new()
    {
        Pronouns = new(StringComparer.Ordinal)
        {
            "أنا", "أنت", "أنتِ", "هو", "هي", "نحن",
            "أنتم", "أنتن", "هم", "هن", "إياه", "إياها"
        },
        Stopwords = new(StringComparer.Ordinal)
        {
            "في", "من", "إلى", "على", "عن", "مع", "هذا", "هذه",
            "ذلك", "تلك", "الذي", "التي", "الذين", "و", "أو", "ثم",
            "لكن", "إن", "أن", "كان", "كانت", "لا", "لم", "لن",
            "قد", "ما", "هل", "كل", "بعض", "غير", "بين", "أمام",
            "خلف", "فوق", "تحت", "عند", "حتى", "إذا", "لو", "كما",
            "أنا", "أنت", "هو", "هي", "نحن", "أنتم", "هم"
        },
        KnownVerbs = new(StringComparer.Ordinal)
        {
            "درس", "يدرس", "تدرس", "أدرس", "ندرس",
            "طور", "يطور", "تطور", "نطور",
            "برمج", "يبرمج", "تبرمج", "نبرمج",
            "حلل", "يحلل", "تحلل", "نحلل",
            "بنى", "يبني", "تبني", "نبني",
            "تعلم", "يتعلم", "تتعلم", "نتعلم",
            "أراد", "يريد", "تريد", "نريد",
            "استخدم", "يستخدم", "تستخدم", "نستخدم",
            "كان", "يكون"
        },
        KnownNouns = new(StringComparer.Ordinal)
        {
            "برمجة", "نظام", "أنظمة", "هندسة", "تطبيق", "تطبيقات",
            "طالب", "طلاب", "نص", "نصوص", "مشروع", "مشاريع",
            "برنامج", "برامج", "حاسوب", "حواسيب", "لغة", "لغات",
            "تحليل", "خوارزمية", "خوارزميات", "قاعدة", "بيانات",
            "مستخدم", "مستخدمون", "ملف", "ملفات", "نتيجة", "نتائج",
            "تقرير", "تقارير"
        },
        // El árabe conjuga con prefijos (ي/ت/ن/أ para presente), no solo sufijos;
        // se listan aquí como "sufijos" que el detector heurístico también
        // revisa al final de la palabra, cubriendo participios y formas
        // derivadas comunes (-ة para nombres de acción/participio femenino).
        VerbSuffixes = ["ون", "ين", "ات"],
        NounSuffixes = ["ية", "ات", "ون"],
        PluralSuffixes = ["ات", "ون", "ين"],
        IrregularVerbLemmas = new(StringComparer.Ordinal)
        {
            ["يدرس"] = "درس", ["تدرس"] = "درس", ["أدرس"] = "درس", ["ندرس"] = "درس",
            ["يطور"] = "طور", ["تطور"] = "طور", ["نطور"] = "طور",
            ["يبرمج"] = "برمج", ["تبرمج"] = "برمج", ["نبرمج"] = "برمج",
            ["يحلل"] = "حلل", ["تحلل"] = "حلل", ["نحلل"] = "حلل",
            ["يبني"] = "بنى", ["تبني"] = "بنى", ["نبني"] = "بنى",
            ["يتعلم"] = "تعلم", ["تتعلم"] = "تعلم", ["نتعلم"] = "تعلم",
            ["يريد"] = "أراد", ["تريد"] = "أراد", ["نريد"] = "أراد",
            ["يستخدم"] = "استخدم", ["تستخدم"] = "استخدم", ["نستخدم"] = "استخدم",
            ["يكون"] = "كان"
        },
        IrregularNounLemmas = new(StringComparer.Ordinal)
        {
            ["أنظمة"] = "نظام", ["تطبيقات"] = "تطبيق", ["طلاب"] = "طالب",
            ["نصوص"] = "نص", ["مشاريع"] = "مشروع", ["برامج"] = "برنامج",
            ["حواسيب"] = "حاسوب", ["لغات"] = "لغة", ["خوارزميات"] = "خوارزمية",
            ["مستخدمون"] = "مستخدم", ["ملفات"] = "ملف", ["نتائج"] = "نتيجة",
            ["تقارير"] = "تقرير"
        },
        // Las palabras árabes suelen ser más cortas en promedio (no llevan
        // vocales largas escritas); se baja el mínimo para no descartar
        // sustantivos válidos de 3 letras.
        MinNounHeuristicLength = 3,
        MinVerbHeuristicLength = 3,
        UsesCapitalizationForNames = false
    };

    private static LanguageProfile BuildChinese() => new()
    {
        Pronouns = new(StringComparer.Ordinal)
        {
            "我", "你", "您", "他", "她", "它",
            "我们", "你们", "他们", "她们", "它们", "自己"
        },
        Stopwords = new(StringComparer.Ordinal)
        {
            "的", "了", "在", "是", "我", "有", "和", "就", "不",
            "人", "都", "一", "上", "也", "很", "到", "说", "要",
            "去", "你", "会", "着", "看", "好", "自己", "这", "那",
            "与", "或", "但", "因为", "所以", "如果", "而", "被",
            "把", "让", "对", "从", "向", "为", "之", "其", "此", "没有"
        },
        KnownVerbs = new(StringComparer.Ordinal)
        {
            "学习", "开发", "编程", "分析", "构建", "编写",
            "使用", "想要", "创建", "运行", "测试"
        },
        KnownNouns = new(StringComparer.Ordinal)
        {
            "编程", "系统", "工程", "应用", "学生", "文本",
            "项目", "程序", "软件", "计算机", "语言", "分析",
            "算法", "数据库", "用户", "文件", "结果", "报告", "数据"
        },
        // El chino no conjuga ni declina con sufijos alfabéticos como los
        // idiomas anteriores, así que estas listas quedan vacías a
        // propósito: la detección para chino depende casi por completo de
        // las listas conocidas (KnownVerbs/KnownNouns) y del tokenizador
        // por coincidencia máxima (ver TokenizeChinese), no de heurísticas
        // de sufijo ni de lematización por recorte.
        VerbSuffixes = [],
        NounSuffixes = [],
        PluralSuffixes = [],
        IrregularVerbLemmas = new(StringComparer.Ordinal),
        IrregularNounLemmas = new(StringComparer.Ordinal),
        MinNounHeuristicLength = 1,
        MinVerbHeuristicLength = 1,
        UsesCapitalizationForNames = false
    };
}

public class DetectedEntity
{
    public string Value { get; set; } = string.Empty;

    public string CanonicalValue { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string? Language { get; set; }
}

public class AnalysisOutput
{
    public int TotalWordCount { get; set; }

    public List<WordFrequency> MostFrequentWords { get; set; } = [];

    public List<WordFrequency> LeastFrequentWords { get; set; } = [];

    public List<string> PersonalPronouns { get; set; } = [];

    public List<string> PersonNames { get; set; } = [];

    public List<string> RootNouns { get; set; } = [];

    public List<string> RootVerbs { get; set; } = [];

    public Dictionary<string, object> OtherClassifications { get; set; } = [];
}

public class WordFrequency
{
    public string Word { get; set; } = null!;

    public int Count { get; set; }
}
