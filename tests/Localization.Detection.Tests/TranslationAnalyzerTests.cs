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
    public async Task Analyzer_IndexerSyntax_FlagsNonConstantAndInvalidMessageAsync()
    {
        // The optional indexer form is a first-class translation site: a non-constant key (APL0001) and invalid
        // ICU (APL0002) are real bugs whichever syntax is used, so the editor must flag them here too.
        const string Source = """
            using ArchPillar.Extensions.Localization;

            public interface ILoc
            {
                string this[[Translatable] string key, [TranslationDefault] string message] { get; }
            }

            public class Consumer
            {
                public void Run(ILoc loc, string dynamicKey)
                {
                    _ = loc["ok", "OK"];
                    _ = loc[dynamicKey, "Dynamic"];
                    _ = loc["broken", "{count, plural, one {x}"];
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.Contains(diagnostics, d => d.Id == "APL0001");
        Assert.Contains(diagnostics, d => d.Id == "APL0002");
    }

    [Fact]
    public async Task Analyzer_StringLocalizerIndexer_StaysQuietInTheEditorAsync()
    {
        // The BCL IStringLocalizer indexer cannot carry the attributes and is extraction-only; migrating code
        // must not be lit up with diagnostics, not even a non-constant lookup.
        const string Source = """
            using Microsoft.Extensions.Localization;

            public class Consumer
            {
                public void Run(IStringLocalizer strings, string dynamicKey)
                {
                    _ = strings["greeting"];
                    _ = strings[dynamicKey];
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("APL", StringComparison.Ordinal));
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
    public async Task Analyzer_DuplicateKeyAcrossTwoFiles_ReportsApl0006Async()
    {
        // The conflicting call sites live in different files; the whole-compilation pass must still pair them
        // (a per-file/per-node decision would miss this and depend on analysis order).
        const string Shared = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }
            """;
        const string FileA = """
            public class A { public void Run() => T.Translate("dup", "First wording"); }
            """;
        const string FileB = """
            public class B { public void Run() => T.Translate("dup", "Second wording"); }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(
            RoslynTestHost.CreateCompilation([Shared, FileA, FileB]));

        Assert.Contains(diagnostics, d => d.Id == "APL0006");
    }

    [Fact]
    public async Task Analyzer_SameKeyDifferentCategories_DoesNotReportApl0006Async()
    {
        const string Source = """
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

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.DoesNotContain(diagnostics, d => d.Id == "APL0006");
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

    [Fact]
    public async Task Analyzer_InvalidKeyPattern_DoesNotCrashAndKeepsOtherDiagnosticsAsync()
    {
        // A syntactically invalid pattern must not throw out of the analyzer (Roslyn would surface AD0001
        // and disable every APL diagnostic); it degrades to no key-pattern check while the rest run.
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ArchPillarLocalizationKeyPattern"] = "[unterminated("
        };

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(
            RoslynTestHost.CreateCompilation(Wrap("""
                T.Translate("dup", "First wording");
                T.Translate("dup", "Second wording");
            """)),
            options);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.DoesNotContain(diagnostics, d => d.Id == "APL0008");
        Assert.Contains(diagnostics, d => d.Id == "APL0006");
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
    public async Task Analyzer_ExplicitArrayArgumentsOverload_ValidatesPlaceholdersApl0003Apl0004Async()
    {
        // The arguments are passed as an explicit (string, object?)[] (the Localized<TSelf> shape), not a
        // params array; placeholder/argument validation must still run.
        const string Source = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate(
                    [Translatable] string key,
                    [TranslationDefault] string message,
                    (string Name, object? Value)[] arguments) => message;
            }

            public class Consumer
            {
                public void Run() => T.Translate("greet", "Hello {name}", [("extra", 1)]);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.Contains(diagnostics, d => d.Id == "APL0003"); // {name} not supplied
        Assert.Contains(diagnostics, d => d.Id == "APL0004"); // "extra" not used
    }

    [Fact]
    public async Task Analyzer_Forwarder_DoesNotReportNonConstantApl0001Async()
    {
        // The documented roll-your-own forwarder threads its own [Translatable] parameter into an inner
        // translatable call; this must not be reported as a non-constant key.
        const string Source = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public static class MyStrings
            {
                public static string Tr([Translatable] string key, [TranslationDefault] string message) => T.Translate(key, message);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RoslynTestHost.RunAnalyzerAsync(RoslynTestHost.CreateCompilation(Source));

        Assert.DoesNotContain(diagnostics, d => d.Id == "APL0001");
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
