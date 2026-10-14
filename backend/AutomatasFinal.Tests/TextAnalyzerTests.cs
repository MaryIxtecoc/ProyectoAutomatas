using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Analysis;

namespace AutomatasFinal.Tests;

public class TextAnalyzerTests
{
    private readonly TextAnalyzer _analyzer = new();

    [Fact]
    public void Texto_vacio_devuelve_resultado_vacio()
    {
        var result = _analyzer.Analyze("   ", AnalysisLanguage.ES);

        Assert.Equal(0, result.TotalWordCount);
        Assert.Empty(result.MostFrequentWords);
    }

    [Fact]
    public void Cuenta_el_total_de_palabras()
    {
        var result = _analyzer.Analyze("El gato come. El perro duerme.", AnalysisLanguage.ES);

        Assert.Equal(6, result.TotalWordCount);
    }

    [Fact]
    public void Calcula_las_palabras_mas_y_menos_repetidas_sin_distinguir_mayusculas()
    {
        var result = _analyzer.Analyze("Sol sol SOL luna luna mar", AnalysisLanguage.ES);

        var top = result.MostFrequentWords.First();
        Assert.Equal("sol", top.Word);
        Assert.Equal(3, top.Count);
        Assert.Contains(result.LeastFrequentWords, w => w.Word == "mar" && w.Count == 1);
    }

    [Fact]
    public void Detecta_pronombres_personales_en_espanol()
    {
        var result = _analyzer.Analyze("Yo leo y ellos escriben mientras nosotros cantamos.", AnalysisLanguage.ES);

        Assert.Contains("yo", result.PersonalPronouns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ellos", result.PersonalPronouns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("nosotros", result.PersonalPronouns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reconoce_nombres_del_diccionario_de_entidades()
    {
        var dictionary = new[]
        {
            new EntityDictionaryEntry { Value = "Xelajú", NormalizedValue = "xelajú", CanonicalValue = "Quetzaltenango", EntityType = "CITY", Language = "ES" },
            new EntityDictionaryEntry { Value = "María", NormalizedValue = "maría", CanonicalValue = "María", EntityType = "PERSON", Language = "ES" }
        };

        var result = _analyzer.Analyze("María viajó a Xelajú con su familia.", AnalysisLanguage.ES, dictionary);

        Assert.Contains("María", result.PersonNames);
        Assert.True(result.OtherClassifications.ContainsKey("language"));
    }

    [Theory]
    [InlineData(AnalysisLanguage.EN, "They read books and we write letters.")]
    [InlineData(AnalysisLanguage.RU, "Они читают книги, а мы пишем письма.")]
    public void Analiza_otros_idiomas_sin_errores(AnalysisLanguage language, string text)
    {
        var result = _analyzer.Analyze(text, language);

        Assert.True(result.TotalWordCount > 0);
        Assert.NotEmpty(result.PersonalPronouns);
    }

    [Fact]
    public void Ayer_no_es_verbo()
    {
        var result = new TextAnalyzer().Analyze("Ayer fuimos al mercado.", AnalysisLanguage.ES);

        Assert.DoesNotContain("ayer", result.RootVerbs);
    }

    [Fact]
    public void Infinitivo_conserva_su_forma_raiz()
    {
        var result = new TextAnalyzer().Analyze("Quiero analizar los datos.", AnalysisLanguage.ES);

        Assert.Contains("analizar", result.RootVerbs);
        Assert.DoesNotContain("analiz", result.RootVerbs);
    }

    [Fact]
    public void Palabra_comun_con_mayuscula_no_es_persona()
    {
        var result = new TextAnalyzer().Analyze("Casa casa CASA. casa, ¡Casa!", AnalysisLanguage.ES);

        Assert.DoesNotContain("Casa", result.PersonNames);
    }
}
