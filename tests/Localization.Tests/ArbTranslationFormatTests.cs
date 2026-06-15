using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class ArbTranslationFormatTests
{
    private static readonly ArbTranslationFormat _format = new();

    [Fact]
    public void Metadata_DescribesArb()
    {
        Assert.Equal("arb", _format.FormatId);
        Assert.Contains(".arb", _format.Extensions);
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.IcuPlural));
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.ExplicitState));
    }

    [Fact]
    public async Task RoundTrip_PreservesEntryFieldsAndStateAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "home.greeting",
                    SourceMessage = "Hello {name}",
                    TranslatedMessage = "Hallo {name}",
                    Context = "home page",
                    Comment = "A greeting",
                    PreviousSource = "Hi {name}",
                    Placeholders = ["name"],
                    References = [new SourceReference("Home.cs", 12, 5)],
                    SourceFingerprint = "abc123",
                    State = TranslationState.Translated
                }
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        CatalogEntry entry = Assert.Single(roundTripped.Entries);
        Assert.Equal("de", roundTripped.Culture);
        Assert.Equal("home.greeting", entry.Key);
        Assert.Equal("Hallo {name}", entry.TranslatedMessage);
        // The source text survives translation (preserved under x-source), so the translator keeps the original.
        Assert.Equal("Hello {name}", entry.SourceMessage);
        Assert.Equal("home page", entry.Context);
        Assert.Equal("A greeting", entry.Comment);
        Assert.Equal("Hi {name}", entry.PreviousSource);
        Assert.Equal(["name"], entry.Placeholders);
        Assert.Equal("abc123", entry.SourceFingerprint);
        Assert.Equal(TranslationState.Translated, entry.State);
        SourceReference reference = Assert.Single(entry.References);
        Assert.Equal(new SourceReference("Home.cs", 12, 5), reference);
    }

    [Fact]
    public async Task RoundTrip_PreservesIcuPluralVerbatimAsync()
    {
        const string Plural = "{count, plural, one {# item} other {# items}}";

        Catalog roundTripped = await RoundTripAsync(SingleEntry("items", Plural));

        Assert.Equal(Plural, Assert.Single(roundTripped.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task Write_IsByteStableAcrossReadWriteCyclesAsync()
    {
        var first = await WriteAsync(SingleEntry("greeting", "Hallo"));
        var second = await WriteAsync(await ReadAsync(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Read_ParsesFlutterStyleArbAsync()
    {
        const string Arb = """
            {
              "@@locale": "en",
              "greeting": "Hello {name}",
              "@greeting": {
                "description": "A greeting",
                "placeholders": { "name": {} }
              }
            }
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Arb));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("en", catalog.Culture);
        Assert.Equal("Hello {name}", entry.TranslatedMessage);
        Assert.Equal("A greeting", entry.Comment);
        Assert.Equal(["name"], entry.Placeholders);
    }

    [Fact]
    public async Task Write_EmitsLocaleAndUnescapedUnicodeAsync()
    {
        var text = Encoding.UTF8.GetString(await WriteAsync(SingleEntry("city", "München")));

        Assert.Contains("\"@@locale\": \"de\"", text);
        Assert.Contains("München", text);
        Assert.DoesNotContain("\r\n", text);
    }

    [Fact]
    public async Task Read_AppliesTopLevelCategoryAsDefaultAsync()
    {
        const string Arb = """
            {
              "@@locale": "de",
              "@@x-category": "Acme.Todo.TodoStrings",
              "add": "Hinzufügen",
              "@add": { "x-state": "Translated", "x-source-fingerprint": "fp" }
            }
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Arb));

        Assert.Equal("Acme.Todo.TodoStrings", Assert.Single(catalog.Entries).Category);
    }

    [Fact]
    public async Task RoundTrip_PreservesEntryCategoryAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "add",
                    Category = "Acme.Todo.TodoStrings",
                    SourceMessage = "Add",
                    TranslatedMessage = "Hinzufügen",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal("Acme.Todo.TodoStrings", Assert.Single(roundTripped.Entries).Category);
    }

    [Fact]
    public async Task Read_NonStringValue_SkipsItButKeepsTheRestAsync()
    {
        // A non-string message value must not drop the whole file.
        const string Arb = """
            {
              "@@locale": "en",
              "count": 5,
              "greeting": "Hello"
            }
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Arb));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("greeting", entry.Key);
        Assert.Equal("Hello", entry.TranslatedMessage);
    }

    [Fact]
    public async Task Write_TranslatedEntry_EmitsXSourceSoTheOriginalIsKeptAsync()
    {
        var text = Encoding.UTF8.GetString(await WriteAsync(new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "save",
                    SourceMessage = "Save",
                    TranslatedMessage = "Speichern",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        }));

        // The value is the translation; the original source is preserved alongside it under x-source.
        Assert.Contains("\"save\": \"Speichern\"", text);
        Assert.Contains("\"x-source\": \"Save\"", text);
    }

    [Fact]
    public async Task Write_UntranslatedEntry_OmitsXSourceSinceValueIsTheSourceAsync()
    {
        // An untranslated entry's value already is the source, so x-source would be redundant and is not written.
        var text = Encoding.UTF8.GetString(await WriteAsync(new Catalog
        {
            Culture = "de",
            Entries = [new CatalogEntry { Key = "save", SourceMessage = "Save", SourceFingerprint = "fp" }]
        }));

        Assert.DoesNotContain("\"x-source\":", text);
    }

    [Fact]
    public async Task Read_UntranslatedEntry_HasNoTranslationSoItPicksUpNewSourceAsync()
    {
        const string Arb = """
            {
              "@@locale": "de",
              "::home": "Home",
              "@::home": { "x-state": "NeedsTranslation", "x-source-fingerprint": "fp" }
            }
            """;

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Arb));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("home", entry.Key);
        Assert.Null(entry.TranslatedMessage);
        Assert.Equal("Home", entry.SourceMessage);
    }

    [Fact]
    public async Task Write_GlobalKey_EmitsBareMemberAsync()
    {
        // A global (uncategorized) key is its own member — no "::" prefix — matching standard ARB so a
        // translation tool pairs it with the source template by id.
        var text = Encoding.UTF8.GetString(await WriteAsync(SingleEntry("home.greeting", "Hallo")));

        Assert.Contains("\"home.greeting\": \"Hallo\"", text);
        Assert.DoesNotContain("::home.greeting", text);
    }

    [Fact]
    public async Task RoundTrip_KeyBeginningWithAt_IsPreservedAsync()
    {
        // A key starting with '@' is escaped to the qualified member "::@weird", never mistaken for metadata.
        Catalog roundTripped = await RoundTripAsync(SingleEntry("@weird", "value"));

        Assert.Equal("@weird", Assert.Single(roundTripped.Entries).Key);
    }

    [Fact]
    public async Task RoundTrip_SameKeyInTwoCategories_KeepsBothWithTheirOwnCategoryAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries =
            [
                Entry("save", "Acme.Labels", "Speichern"),
                Entry("save", "Acme.Buttons", "Sichern")
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal(2, roundTripped.Entries.Count);
        Assert.Equal("Speichern", Single(roundTripped, "save", "Acme.Labels").TranslatedMessage);
        Assert.Equal("Sichern", Single(roundTripped, "save", "Acme.Buttons").TranslatedMessage);
    }

    private static CatalogEntry Entry(string key, string category, string message) => new()
    {
        Key = key,
        Category = category,
        SourceMessage = message,
        TranslatedMessage = message,
        SourceFingerprint = "fp",
        State = TranslationState.Translated
    };

    private static CatalogEntry Single(Catalog catalog, string key, string category) =>
        catalog.Entries.Single(e => e.Key == key && e.Category == category);

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
