using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Analysis;

namespace AutomatasFinal.Tests;

public class NlpResultMergerTests
{
    private static AnalysisOutput Manual(string text, AnalysisLanguage lang = AnalysisLanguage.ES,
        IEnumerable<EntityDictionaryEntry>? dictionary = null)
        => new TextAnalyzer().Analyze(text, lang, dictionary);

    [Fact]
    public void Usa_verbos_y_sustantivos_del_servicio_NLP()
    {
        var manual = Manual("Juan camina. Ayer comimos.");
        var nlp = new NlpAnalysisResult
        {
            Engine = "spacy",
            Model = "es_core_news_md",
            RootVerbs = ["caminar", "comer"],
            RootNouns = ["gato"],
            VerbCount = 2,
            NounCount = 1,
            Entities = new() { ["PERSON"] = [new NlpEntity { Text = "Juan", Canonical = "Juan" }] }
        };

        var merged = NlpResultMerger.Merge(manual, nlp, AnalysisLanguage.ES);

        Assert.Equal(new List<string> { "caminar", "comer" }, merged.RootVerbs);
        Assert.DoesNotContain("ayer", merged.RootVerbs);
        Assert.Equal(new List<string> { "Juan" }, merged.PersonNames);
        Assert.Equal("spacy:es_core_news_md", (string)merged.OtherClassifications["analysisEngine"]);
    }

    [Fact]
    public void Conserva_el_conteo_de_palabras_del_analizador_propio()
    {
        var manual = Manual("casa casa perro");
        var merged = NlpResultMerger.Merge(manual, new NlpAnalysisResult { Engine = "spacy" }, AnalysisLanguage.ES);

        Assert.Equal(3, merged.TotalWordCount);
        Assert.Equal("casa", merged.MostFrequentWords[0].Word);
    }

    [Fact]
    public void Personas_ya_no_incluye_palabras_con_mayuscula_que_no_son_nombres()
    {
        var manual = Manual("Casa casa CASA. casa, ¡Casa!");
        Assert.Contains("Casa", manual.PersonNames); // comportamiento anterior

        var merged = NlpResultMerger.Merge(manual, new NlpAnalysisResult { Engine = "spacy" }, AnalysisLanguage.ES);

        Assert.Empty(merged.PersonNames);
    }

    [Fact]
    public void En_chino_usa_la_segmentacion_del_servicio()
    {
        var manual = Manual("我喜欢学习中文", AnalysisLanguage.ZH);
        var nlp = new NlpAnalysisResult
        {
            Engine = "spacy",
            Words = ["我", "喜欢", "学习", "中文"],
            PersonalPronouns = ["我"]
        };

        var merged = NlpResultMerger.Merge(manual, nlp, AnalysisLanguage.ZH);

        Assert.Equal(4, merged.TotalWordCount);
        Assert.Contains("我", merged.PersonalPronouns);
    }

    [Fact]
    public void Sin_servicio_queda_marcado_como_manual()
    {
        var manual = Manual("Hola mundo");
        NlpResultMerger.MarkManual(manual);

        Assert.Equal("manual", (string)manual.OtherClassifications["analysisEngine"]);
        Assert.Equal(2, manual.TotalWordCount);
    }

    [Fact]
    public void Une_variantes_del_mismo_nombre()
    {
        var names = NlpResultMerger.UnifyPersonNames(new[] { "María", "María López", "Carlos" });

        Assert.Equal(new List<string> { "Carlos", "María López" }, names);
    }

    [Fact]
    public void Si_afirmativo_no_entra_como_pronombre_cuando_hay_NLP()
    {
        var manual = Manual("Ellos respondieron que sí.");
        var nlp = new NlpAnalysisResult
        {
            Engine = "stanza",
            Words = ["ellos", "respondieron", "que", "sí"],
            PersonalPronouns = ["ellos"]
        };

        var merged = NlpResultMerger.Merge(manual, nlp, AnalysisLanguage.ES);

        Assert.Equal(new List<string> { "ellos" }, merged.PersonalPronouns);
    }
}
