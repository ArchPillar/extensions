using System.Globalization;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class LocalizerTests : IDisposable
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _deAt = CultureInfo.GetCultureInfo("de-AT");
    private static readonly CultureInfo _fr = CultureInfo.GetCultureInfo("fr");

    private readonly List<string> _directories = [];
    private readonly List<CatalogStore> _stores = [];

    [Fact]
    public void Translate_NoFiles_RendersInCodeDefault()
    {
        Localizer localizer = Make(NewDirectory());

        Assert.Equal("Hello Ada", localizer.Translate(_de, "greeting", "Hello {name}", null, ("name", "Ada")));
    }

    [Fact]
    public void Translate_Override_IsUsedForItsCulture()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "Hallo {name}"));
        Localizer localizer = Make(directory);

        Assert.Equal("Hallo Ada", localizer.Translate(_de, "greeting", "Hello {name}", null, ("name", "Ada")));
    }

    [Fact]
    public void Translate_ParentCulture_IsUsedForChildCulture()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "Hallo"));
        Localizer localizer = Make(directory);

        Assert.Equal("Hallo", localizer.Translate(_deAt, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_ChildCulture_TakesPrecedenceOverParent()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "Hallo"));
        WriteArb(directory, "de-AT", Entry("greeting", "Servus"));
        Localizer localizer = Make(directory);

        Assert.Equal("Servus", localizer.Translate(_deAt, "greeting", "Hello", null));
        Assert.Equal("Hallo", localizer.Translate(_de, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_MissingKeyForCulture_DegradesPerCulture()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "Hallo"));
        Localizer localizer = Make(directory);

        Assert.Equal("Hallo", localizer.Translate(_de, "greeting", "Hello", null));
        Assert.Equal("Hello", localizer.Translate(_fr, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_UntranslatedEntry_FallsThroughToDefault()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", string.Empty, "NeedsTranslation"));
        Localizer localizer = Make(directory);

        Assert.Equal("Hello", localizer.Translate(_de, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_SourceCultureFile_IsNotLoadedAsOverride()
    {
        var directory = NewDirectory();
        WriteArb(directory, "en", Entry("greeting", "FROM FILE"));
        Localizer localizer = Make(directory);

        Assert.Equal("Hello", localizer.Translate(_en, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_Plural_UsesTargetCulturePluralRules()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("items", "{count, plural, one {# Datei} other {# Dateien}}"));
        Localizer localizer = Make(directory);

        const string Default = "{count, plural, one {# file} other {# files}}";
        Assert.Equal("1 Datei", localizer.Translate(_de, "items", Default, null, ("count", 1)));
        Assert.Equal("2 Dateien", localizer.Translate(_de, "items", Default, null, ("count", 2)));
    }

    [Fact]
    public void Translate_DefaultFallback_UsesSourceCulturePluralRules()
    {
        // No Japanese override: the English default must render with English (source) plural rules,
        // not Japanese rules (which have only "other"), so "1 file" stays grammatical — not "1 files".
        Localizer localizer = Make(NewDirectory());
        var japanese = CultureInfo.GetCultureInfo("ja");

        const string Default = "{count, plural, one {# file} other {# files}}";
        Assert.Equal("1 file", localizer.Translate(japanese, "inbox", Default, null, ("count", 1)));
        Assert.Equal("3 files", localizer.Translate(japanese, "inbox", Default, null, ("count", 3)));
    }

    [Fact]
    public void Translate_NonEnglishSource_DoesNotAssumeEnglish()
    {
        // Source language is German; the in-code default is German and must be excluded from overrides
        // when a de.arb exists, while still rendering correctly with German rules.
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "FROM FILE"));
        Localizer localizer = Over(new LocalizerOptions
        {
            TranslationsDirectory = directory,
            SourceCulture = "de"
        });

        // de is now the source culture, so de.arb is ignored and the in-code German default wins.
        Assert.Equal("Hallo", localizer.Translate(_de, "greeting", "Hallo", null));
    }

    [Fact]
    public void Translate_Context_DisambiguatesKey()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", ContextEntry("post", "Posten", "menu"));
        Localizer localizer = Make(directory);

        // Same key, different context: only the "menu" context is overridden.
        Assert.Equal("Posten", localizer.Translate(_de, "post", "Post", "menu"));
        Assert.Equal("Post", localizer.Translate(_de, "post", "Post", "verb"));
    }

    [Fact]
    public void Translate_MissingArgument_PassesThroughByDefault()
    {
        Localizer localizer = Make(NewDirectory());

        Assert.Equal("Hi {name}", localizer.Translate(_de, "greeting", "Hi {name}", null));
    }

    [Fact]
    public void Reload_PicksUpNewlyAddedFile()
    {
        var directory = NewDirectory();
        var store = new CatalogStore(new LocalizerOptions { TranslationsDirectory = directory, SourceCulture = "en" });
        _stores.Add(store);
        var localizer = new Localizer(store);
        Assert.Equal("Hello", localizer.Translate(_de, "greeting", "Hello", null));

        WriteArb(directory, "de", Entry("greeting", "Hallo"));
        store.Reload();

        Assert.Equal("Hallo", localizer.Translate(_de, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_MixedFormats_PrefersXliffOverArbByDefault()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "from arb"));
        WriteCatalog(new XliffTranslationFormat(), directory, "de.xliff", DeCatalog("greeting", "from xliff"));
        Localizer localizer = Make(directory);

        Assert.Equal("from xliff", localizer.Translate(_de, "greeting", "Hello", null));
    }

    [Fact]
    public void Translate_FormatPrecedence_IsConfigurable()
    {
        var directory = NewDirectory();
        WriteArb(directory, "de", Entry("greeting", "from arb"));
        WriteCatalog(new XliffTranslationFormat(), directory, "de.xliff", DeCatalog("greeting", "from xliff"));
        Localizer localizer = Over(new LocalizerOptions
        {
            TranslationsDirectory = directory,
            SourceCulture = "en",
            FormatPrecedence = ["arb", "xliff", "po"]
        });

        Assert.Equal("from arb", localizer.Translate(_de, "greeting", "Hello", null));
    }

    [Fact]
    public void FromCatalogs_UsesSuppliedOverride_WithoutTouchingDisk()
    {
        var localizer = Localizer.FromCatalogs(
            [DeCatalog("greeting", "Hallo")],
            new LocalizerOptions { SourceCulture = "en" });

        Assert.Equal("Hallo", localizer.Translate(_de, "greeting", "Hello", null));
        Assert.Equal("Hello", localizer.Translate(_fr, "greeting", "Hello", null));
    }

    [Fact]
    public void FromCatalogs_SkipsSourceCultureCatalog()
    {
        var enCatalog = new Catalog
        {
            Culture = "en",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "greeting",
                    SourceMessage = "FROM CATALOG",
                    TranslatedMessage = "FROM CATALOG",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        };

        var localizer = Localizer.FromCatalogs([enCatalog], new LocalizerOptions { SourceCulture = "en" });

        Assert.Equal("Hello", localizer.Translate(_en, "greeting", "Hello", null));
    }

    [Fact]
    public void FromCatalogs_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Localizer.FromCatalogs(null!));

    public void Dispose()
    {
        foreach (CatalogStore store in _stores)
        {
            store.Dispose();
        }

        foreach (var directory in _directories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Catalog DeCatalog(string key, string message) => new()
    {
        Culture = "de",
        Headers = new Dictionary<string, string> { ["srcLang"] = "en" },
        Entries =
        [
            new CatalogEntry
            {
                Key = key,
                SourceMessage = message,
                TranslatedMessage = message,
                SourceFingerprint = "fp",
                State = TranslationState.Translated
            }
        ]
    };

    private static void WriteCatalog(ITranslationFormat format, string directory, string fileName, Catalog catalog)
    {
        using FileStream stream = File.Create(Path.Combine(directory, fileName));
        format.WriteAsync(stream, catalog, CancellationToken.None).GetAwaiter().GetResult();
    }

    private Localizer Make(string directory) =>
        Over(new LocalizerOptions { TranslationsDirectory = directory, SourceCulture = "en" });

    // Builds a directory-backed localizer over a tracked CatalogStore (disposed with the fixture).
    private Localizer Over(LocalizerOptions options)
    {
        var store = new CatalogStore(options);
        _stores.Add(store);
        return new Localizer(store);
    }

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aploc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }

    [Fact]
    public void Directory_TwoSameFormatFilesOverlap_ResolveADeterministicWinner()
    {
        var directory = NewDirectory();
        WriteArbFile(directory, "a.arb", "de", Entry("k", "fromA"));
        WriteArbFile(directory, "b.arb", "de", Entry("k", "fromB"));

        Localizer localizer = Make(directory);

        // Equal-rank files (same format, same culture) are ordered by ordinal path, so the later file wins
        // deterministically rather than depending on the file system's enumeration order.
        Assert.Equal(
            "fromB",
            localizer.Translate(System.Globalization.CultureInfo.GetCultureInfo("de"), "k", "k", context: null));
    }

    private static void WriteArbFile(string directory, string fileName, string culture, string entriesJson)
    {
        var json = $$"""
            {
              "@@locale": "{{culture}}",
            {{entriesJson}}
            }
            """;
        File.WriteAllText(Path.Combine(directory, fileName), json);
    }

    private static void WriteArb(string directory, string culture, string entriesJson)
    {
        var json = $$"""
            {
              "@@locale": "{{culture}}",
            {{entriesJson}}
            }
            """;
        File.WriteAllText(Path.Combine(directory, culture + ".arb"), json);
    }

    private static string Entry(string key, string message, string state = "Translated") =>
        $$"""
              "{{key}}": "{{message}}",
              "@{{key}}": { "x-state": "{{state}}", "x-source-fingerprint": "fp" }
        """;

    private static string ContextEntry(string key, string message, string context) =>
        $$"""
              "{{key}}": "{{message}}",
              "@{{key}}": { "context": "{{context}}", "x-state": "Translated", "x-source-fingerprint": "fp" }
        """;
}
