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

    [Fact]
    public void Generator_GroupsKeysByCategoryIntoNestedClasses()
    {
        const string Scoped = """
            using ArchPillar.Extensions.Localization;

            public interface IScoped<[TranslationScope] T>
            {
                string Translate([Translatable] string key, [TranslationDefault] string message);
            }

            public sealed class Save;
            public sealed class Cancel;

            public sealed class Consumer(IScoped<Save> save, IScoped<Cancel> cancel)
            {
                public void Run()
                {
                    save.Translate("label", "Save");
                    cancel.Translate("label", "Cancel");
                }
            }
            """;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(RoslynTestHost.CreateCompilation(Scoped)).GetRunResult();
        var registry = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == "TranslationKeys.g.cs")
            .SourceText
            .ToString();

        Assert.Contains("public static class Save", registry);
        Assert.Contains("public static class Cancel", registry);
        // The same key "label" lives once per category, with no top-level collision.
        Assert.Equal(2, CountOccurrences(registry, "public const string Label = \"label\";"));
    }

    [Fact]
    public void Generator_ExtractsIStringLocalizerIndexerSites()
    {
        // The generator (not just the test-only whole-compilation Detect) must pick up indexer call sites.
        const string Indexer = """
            using Microsoft.Extensions.Localization;

            public sealed class Home;

            public sealed class Consumer(IStringLocalizer<Home> loc)
            {
                public string Title() => loc["Email is required"];
            }
            """;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(RoslynTestHost.CreateCompilation(Indexer)).GetRunResult();
        var template = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == "LocalizationTemplate.g.cs")
            .SourceText
            .ToString();

        Assert.Contains("Email is required", DecodeTemplate(template));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
