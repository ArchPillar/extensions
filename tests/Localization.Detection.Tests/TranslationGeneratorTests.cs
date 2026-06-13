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

        var registry = GeneratedFrom(Indexer, "TranslationKeys.g.cs");

        Assert.Contains("Email is required", registry);
    }

    [Fact]
    public void Generator_ExtractsNullConditionalIndexerSites()
    {
        // A defensive loc?["key"] is a null-conditional indexer (an element binding, not a plain element
        // access); it must still be detected so the key reaches the registry and the analyzer.
        const string NullConditional = """
            using Microsoft.Extensions.Localization;

            public sealed class Home;

            public sealed class Consumer(IStringLocalizer<Home>? loc)
            {
                public string? Title() => loc?["Email is required"];
            }
            """;

        var registry = GeneratedFrom(NullConditional, "TranslationKeys.g.cs");

        Assert.Contains("Email is required", registry);
    }

    [Fact]
    public void Generator_KeyWithControlCharacters_EmitsCompilableRegistry()
    {
        // A key carrying a newline and tab (realistic for an IStringLocalizer indexer literal) must be
        // escaped, or the generated const literal spans lines and fails to compile.
        const string ControlKey = """
            using Microsoft.Extensions.Localization;

            public sealed class Home;

            public sealed class Consumer(IStringLocalizer<Home> loc)
            {
                public string Title() => loc["line1\nline2\tend"];
            }
            """;

        var registry = GeneratedFrom(ControlKey, "TranslationKeys.g.cs");

        Assert.Contains("\"line1\\nline2\\tend\"", registry);
        AssertCompiles(registry);
    }

    [Fact]
    public void Generator_GlobalKeyCollidingWithCategoryName_DoesNotProduceDuplicateMember()
    {
        // A global key whose identifier matches a category class name shares the TranslationKeys member
        // scope; the two must be disambiguated rather than emitted as colliding members.
        const string Collide = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public interface IScoped<[TranslationScope] TScope>
            {
                string Translate([Translatable] string key, [TranslationDefault] string message);
            }

            public sealed class Save;

            public sealed class Consumer(IScoped<Save> save)
            {
                public void Run()
                {
                    T.Translate("save", "Save");
                    save.Translate("label", "Label");
                }
            }
            """;

        var registry = GeneratedFrom(Collide, "TranslationKeys.g.cs");

        AssertCompiles(registry);
    }

    private static void AssertCompiles(string registry)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(registry);
        var compilation = CSharpCompilation.Create(
            "RegistryProbe",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

        Assert.Empty(errors);
    }

    private static string GeneratedFrom(string source, string hintName)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(RoslynTestHost.CreateCompilation(source)).GetRunResult();
        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == hintName)
            .SourceText
            .ToString();
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
}
