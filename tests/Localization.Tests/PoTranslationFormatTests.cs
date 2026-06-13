using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class PoTranslationFormatTests
{
    private static readonly PoTranslationFormat _format = new();

    [Fact]
    public void Metadata_DescribesPortableObject()
    {
        Assert.Equal("po", _format.FormatId);
        Assert.Contains(".po", _format.Extensions);
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.NativePlural));
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.Comments));
    }

    [Fact]
    public async Task RoundTrip_NonPlural_PreservesFieldsAndInferredStateAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "greeting",
                    Context = "home",
                    SourceMessage = "Hello {name}",
                    TranslatedMessage = "Hallo {name}",
                    Comment = "A greeting",
                    PreviousSource = "Hi {name}",
                    References = [new SourceReference("Home.cs", 12, 5)],
                    SourceFingerprint = "abc123",
                    State = TranslationState.NeedsReview
                }
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        CatalogEntry entry = Assert.Single(roundTripped.Entries);
        Assert.Equal("greeting", entry.Key);
        Assert.Equal("home", entry.Context);
        Assert.Equal("Hello {name}", entry.SourceMessage);
        Assert.Equal("Hallo {name}", entry.TranslatedMessage);
        Assert.Equal("A greeting", entry.Comment);
        Assert.Equal("Hi {name}", entry.PreviousSource);
        Assert.Equal("abc123", entry.SourceFingerprint);
        Assert.Equal(TranslationState.NeedsReview, entry.State);
        Assert.Equal(new SourceReference("Home.cs", 12, 5), Assert.Single(entry.References));
    }

    [Fact]
    public async Task RoundTrip_Plural_ConvertsToNativeGettextAndBackAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "inbox.count",
                    SourceMessage = "{count, plural, one {# file} other {# files}}",
                    TranslatedMessage = "{count, plural, one {# Datei} other {# Dateien}}",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        };

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));
        Assert.Contains("msgid_plural \"# files\"", text);
        Assert.Contains("msgstr[0] \"# Datei\"", text);
        Assert.Contains("msgstr[1] \"# Dateien\"", text);

        Catalog roundTripped = await RoundTripAsync(catalog);
        CatalogEntry entry = Assert.Single(roundTripped.Entries);
        Assert.Equal("{count, plural, one {# file} other {# files}}", entry.SourceMessage);
        Assert.Equal("{count, plural, one {# Datei} other {# Dateien}}", entry.TranslatedMessage);
    }

    [Fact]
    public async Task RoundTrip_UnrepresentableSelect_IsKeptVerbatimAsync()
    {
        const string Select = "{gender, select, male {He} other {They}}";
        Catalog catalog = SingleEntry("g", Select);

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));
        Assert.DoesNotContain("msgid_plural", text);

        Catalog roundTripped = await RoundTripAsync(catalog);
        Assert.Equal(Select, Assert.Single(roundTripped.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task Write_IsByteStableAcrossReadWriteCyclesAsync()
    {
        var first = await WriteAsync(SingleEntry("greeting", "Hallo"));
        var second = await WriteAsync(await ReadAsync(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Read_ParsesPoeditStylePoAsync()
    {
        const string Po = """
            msgid ""
            msgstr ""
            "Language: de\n"
            "Plural-Forms: nplurals=2; plural=(n != 1);\n"

            msgctxt "greeting"
            msgid "Hello"
            msgstr "Hallo"
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Po));

        Assert.Equal("de", catalog.Culture);
        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("greeting", entry.Key);
        Assert.Equal("Hello", entry.SourceMessage);
        Assert.Equal("Hallo", entry.TranslatedMessage);
        Assert.Equal(TranslationState.Translated, entry.State);
    }

    [Fact]
    public async Task RoundTrip_PreservesCategoryAsync()
    {
        Catalog catalog = SingleEntry("save", "Speichern") with
        {
            Entries = [SingleEntry("save", "Speichern").Entries[0] with { Category = "Acme.Todo.TodoStrings" }]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal("Acme.Todo.TodoStrings", roundTripped.Entries[0].Category);
    }

    [Fact]
    public async Task Read_GappedMsgstrIndices_DoesNotCrashAndLoadsTheEntryAsync()
    {
        // msgstr[0] and msgstr[2] with no [1]: the array must size by the highest index, not the count.
        const string Po = """
            msgid ""
            msgstr ""
            "Language: pl\n"

            msgctxt "files"
            msgid "one file"
            msgid_plural "files"
            msgstr[0] "plik"
            msgstr[2] "plikow"
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Po));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("files", entry.Key);
        Assert.Contains("plik", entry.TranslatedMessage);
    }

    [Fact]
    public async Task Read_UnescapesGettextControlEscapes_Async()
    {
        const string Po = """
            msgctxt "k"
            msgid "src"
            msgstr "a\bc\fd"
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Po));

        Assert.Equal("a\bc\fd", Assert.Single(catalog.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task Read_TranslatorComment_IsCapturedAndKeptDistinctFromExtractedAsync()
    {
        const string Po = """
            # please keep this formal
            #. developer note
            msgctxt "k"
            msgid "src"
            msgstr "t"
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Po));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("please keep this formal", entry.TranslatorComment);
        Assert.Equal("developer note", entry.Comment);
    }

    [Fact]
    public async Task RoundTrip_TranslatorComment_SurvivesAndWritesAsHashSpaceAsync()
    {
        Catalog catalog = SingleEntry("greeting", "Hallo") with
        {
            Entries = [SingleEntry("greeting", "Hallo").Entries[0] with { TranslatorComment = "keep it formal" }]
        };

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));
        Assert.Contains("# keep it formal\n", text, StringComparison.Ordinal);

        Catalog roundTripped = await RoundTripAsync(catalog);
        Assert.Equal("keep it formal", Assert.Single(roundTripped.Entries).TranslatorComment);
    }

    [Fact]
    public async Task Read_MultipleReferencesOnOneLine_ParsesEachAsync()
    {
        const string Po = """
            #: Home.cs:12:5 Other.cs:7:3
            msgctxt "k"
            msgid "src"
            msgstr "t"
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Po));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal(2, entry.References.Count);
        Assert.Contains(new SourceReference("Home.cs", 12, 5), entry.References);
        Assert.Contains(new SourceReference("Other.cs", 7, 3), entry.References);
    }

    private static Catalog SingleEntry(string key, string message) => new()
    {
        Culture = "de",
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

    private static async Task<Catalog> RoundTripAsync(Catalog catalog) =>
        await ReadAsync(await WriteAsync(catalog));

    private static async Task<byte[]> WriteAsync(Catalog catalog)
    {
        using var stream = new MemoryStream();
        await _format.WriteAsync(stream, catalog, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<Catalog> ReadAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await _format.ReadAsync(stream, CancellationToken.None);
    }
}
