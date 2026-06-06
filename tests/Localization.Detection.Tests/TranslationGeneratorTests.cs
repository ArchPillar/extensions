using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchPillar.Extensions.Localization.Detection.Tests;

public sealed class TranslationGeneratorTests
{
    private const string Source = """
        using ArchPillar.Extensions.Localization;

        public static class T
        {
            public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
        }

        public class Consumer
        {
            public void Run() => T.Translate("home.title", "Home");
        }
        """;

    [Fact]
    public void Generator_EmitsTypedKeyRegistry()
    {
        var registry = Generated("TranslationKeys.g.cs");

        Assert.Contains("public const string HomeTitle = \"home.title\";", registry);
    }

    [Fact]
    public void Generator_BakesTemplateAttributeWithArbContent()
    {
        var template = Generated("LocalizationTemplate.g.cs");

        Assert.Contains("GeneratedLocalizationTemplate(", template);
        var arb = DecodeTemplate(template);
        Assert.Contains("\"@@locale\": \"en\"", arb);
        Assert.Contains("\"home.title\": \"Home\"", arb);
        Assert.Contains("x-source-fingerprint", arb);
    }

    private static string Generated(string hintName)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(RoslynTestHost.CreateCompilation(Source)).GetRunResult();
        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == hintName)
            .SourceText
            .ToString();
    }

    private static string DecodeTemplate(string template)
    {
        const string Marker = "GeneratedLocalizationTemplate(";
        var open = template.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var close = template.IndexOf(")]", open, StringComparison.Ordinal);
        var parts = template[open..close].Split([", "], StringSplitOptions.None);
        var base64 = parts[2].Trim().Trim('"');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
