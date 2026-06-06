using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ArchPillar.Extensions.Localization.Detection.Tests;

public sealed class TranslationAnalyzerTests
{
    [Fact]
    public async Task Analyzer_ReportsNonConstantAndInvalidMessageAsync()
    {
        const string Source = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public class Consumer
            {
                public void Run(string dynamicKey)
                {
                    T.Translate("ok", "OK");
                    T.Translate(dynamicKey, "Dynamic");
                    T.Translate("broken", "{count, plural, one {x}");
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.Contains(diagnostics, d => d.Id == "APL0001");
        Assert.Contains(diagnostics, d => d.Id == "APL0002");
    }

    [Fact]
    public async Task Analyzer_DuplicateKeyWithConflictingDefault_ReportsApl0006Async()
    {
        const string Source = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public class Consumer
            {
                public void Run()
                {
                    T.Translate("dup", "First wording");
                    T.Translate("dup", "Second wording");
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.Contains(diagnostics, d => d.Id == "APL0006");
    }

    [Fact]
    public async Task Analyzer_PlaceholderNotSupplied_ReportsApl0003Async()
    {
        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(
            Wrap("""T.Translate("greet", "Hello {name}");""")));

        Assert.Contains(diagnostics, d => d.Id == "APL0003");
    }

    [Fact]
    public async Task Analyzer_ArgumentNotUsed_ReportsApl0004Async()
    {
        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(
            Wrap("""T.Translate("greet", "Hello", ("extra", 1));""")));

        Assert.Contains(diagnostics, d => d.Id == "APL0004");
    }

    [Fact]
    public async Task Analyzer_MissingOtherBranch_ReportsApl0005Async()
    {
        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(
            Wrap("""T.Translate("inbox", "{count, plural, one {# message}}");""")));

        Assert.Contains(diagnostics, d => d.Id == "APL0005");
    }

    [Fact]
    public async Task Analyzer_IdenticalTextUnderDifferentKeys_ReportsApl0007Async()
    {
        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(
            Wrap("""
                T.Translate("home.ok", "OK");
                T.Translate("dialog.ok", "OK");
            """)));

        Assert.Contains(diagnostics, d => d.Id == "APL0007");
    }

    [Fact]
    public async Task Analyzer_KeyPattern_ReportsApl0008WhenConfiguredAsync()
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ArchPillarLocalizationKeyPattern"] = "^[a-z.]+$"
        };

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(
            RoslynTestHost.CreateCompilation(Wrap("""T.Translate("BadKey", "X");""")),
            options);

        Assert.Contains(diagnostics, d => d.Id == "APL0008");
    }

    private static string Wrap(string body) => $$"""
        using ArchPillar.Extensions.Localization;

        public static class T
        {
            public static string Translate(
                [Translatable] string key,
                [TranslationDefault] string message,
                params (string Name, object? Value)[] arguments) => message;
        }

        public class Consumer
        {
            public void Run()
            {
                {{body}}
            }
        }
        """;

    [Fact]
    public async Task Analyzer_WithoutLibrary_ReportsNothingAsync()
    {
        const string Source = """
            public class C
            {
                public void M() => System.Console.WriteLine("hi");
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("APL", StringComparison.Ordinal));
    }
}
