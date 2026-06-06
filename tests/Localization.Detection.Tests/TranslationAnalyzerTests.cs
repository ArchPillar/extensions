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
