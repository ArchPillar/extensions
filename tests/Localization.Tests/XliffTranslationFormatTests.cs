using System.Text;
using System.Xml.Linq;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class XliffTranslationFormatTests
{
    private static readonly XliffTranslationFormat _format = new();
    private static readonly XNamespace _ns = "urn:oasis:names:tc:xliff:document:2.1";

    [Fact]
    public void Metadata_DescribesXliff()
    {
        Assert.Equal("xliff", _format.FormatId);
        Assert.Contains(".xliff", _format.Extensions);
        Assert.Contains(".xlf", _format.Extensions);
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.ExplicitState));
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.PreviousSource));
    }

    [Fact]
    public async Task RoundTrip_PreservesSourceTargetNotesAndStateAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Headers = new Dictionary<string, string> { ["srcLang"] = "en" },
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
                    References = [new SourceReference("Home.cs", 12, 5)],
                    SourceFingerprint = "abc123",
                    State = TranslationState.NeedsReview
                }
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal("de", roundTripped.Culture);
        Assert.Equal("en", roundTripped.Headers["srcLang"]);
        CatalogEntry entry = Assert.Single(roundTripped.Entries);
        Assert.Equal("home.greeting", entry.Key);
        Assert.Equal("Hello {name}", entry.SourceMessage);
        Assert.Equal("Hallo {name}", entry.TranslatedMessage);
        Assert.Equal("home page", entry.Context);
        Assert.Equal("A greeting", entry.Comment);
        Assert.Equal("Hi {name}", entry.PreviousSource);
        Assert.Equal("abc123", entry.SourceFingerprint);
        Assert.Equal(TranslationState.NeedsReview, entry.State);
        Assert.Equal(new SourceReference("Home.cs", 12, 5), Assert.Single(entry.References));
    }

    [Fact]
    public async Task RoundTrip_PreservesIcuPluralVerbatimAsync()
    {
        const string Plural = "{count, plural, one {# item} other {# items}}";

        Catalog roundTripped = await RoundTripAsync(Translated("items", Plural));

        Assert.Equal(Plural, Assert.Single(roundTripped.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task Write_IsByteStableAcrossReadWriteCyclesAsync()
    {
        var first = await WriteAsync(Translated("greeting", "Hallo"));
        var second = await WriteAsync(Read(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Read_ParsesXliff21()
    {
        const string Xliff = """
            <?xml version="1.0" encoding="utf-8"?>
            <xliff xmlns="urn:oasis:names:tc:xliff:document:2.1" version="2.1" srcLang="en" trgLang="de">
              <file id="f1">
                <unit id="greeting">
                  <segment state="translated">
                    <source>Hello</source>
                    <target>Hallo</target>
                  </segment>
                </unit>
              </file>
            </xliff>
            """;

        Catalog catalog = Read(Encoding.UTF8.GetBytes(Xliff));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("de", catalog.Culture);
        Assert.Equal("Hello", entry.SourceMessage);
        Assert.Equal("Hallo", entry.TranslatedMessage);
        Assert.Equal(TranslationState.Translated, entry.State);
    }

    [Fact]
    public async Task RoundTrip_PreservesCategoryAsync()
    {
        Catalog source = Translated("save", "Speichern");
        Catalog catalog = source with { Entries = [source.Entries[0] with { Category = "Acme.Todo.TodoStrings" }] };

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal("Acme.Todo.TodoStrings", roundTripped.Entries[0].Category);
    }

    [Fact]
    public void Read_Xliff20_ParsesInsteadOfReturningEmpty()
    {
        // XLIFF 2.0 has the same shape as 2.1; only the namespace's final digit differs.
        const string Xliff = """
            <?xml version="1.0" encoding="utf-8"?>
            <xliff xmlns="urn:oasis:names:tc:xliff:document:2.0" version="2.0" srcLang="en" trgLang="de">
              <file id="f1">
                <unit id="greeting">
                  <segment state="translated">
                    <source>Hello</source>
                    <target>Hallo</target>
                  </segment>
                </unit>
              </file>
            </xliff>
            """;

        Catalog catalog = Read(Encoding.UTF8.GetBytes(Xliff));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("Hello", entry.SourceMessage);
        Assert.Equal("Hallo", entry.TranslatedMessage);
    }

    [Fact]
    public void Read_Xliff12_ThrowsRatherThanReturnEmpty()
    {
        const string Xliff = """
            <?xml version="1.0" encoding="utf-8"?>
            <xliff xmlns="urn:oasis:names:tc:xliff:document:1.2" version="1.2">
              <file source-language="en" target-language="de">
                <body>
                  <trans-unit id="greeting">
                    <source>Hello</source>
                    <target>Hallo</target>
                  </trans-unit>
                </body>
              </file>
            </xliff>
            """;

        Assert.Throws<NotSupportedException>(() => Read(Encoding.UTF8.GetBytes(Xliff)));
    }

    [Fact]
    public async Task RoundTrip_WhitespaceOnlySource_IsPreservedAsync()
    {
        Catalog source = Translated("space", "   ");

        Catalog roundTripped = await RoundTripAsync(source);

        Assert.Equal("   ", roundTripped.Entries[0].SourceMessage);
    }

    [Fact]
    public async Task Write_UsesSourceNameAsFileIdAsync()
    {
        var xml = Encoding.UTF8.GetString(
            await WriteAsync(Translated("greeting", "Hallo"), new CatalogWriteOptions { SourceName = "Acme.Greeting" }));

        Assert.Contains("<file id=\"Acme.Greeting\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_WithoutSourceName_FallsBackToGenericFileIdAsync()
    {
        var xml = Encoding.UTF8.GetString(await WriteAsync(Translated("greeting", "Hallo")));

        Assert.Contains("<file id=\"f1\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_SameKeyInTwoCategories_ProducesDistinctUnitIdsAsync()
    {
        // The reason the fix exists: a bare key as the unit id would emit two <unit id="Active"> in one
        // <file>, which XLIFF 2.1 (§4.3.1.21) forbids. The identity hash keeps them distinct.
        var catalog = new Catalog
        {
            Culture = "de",
            Headers = new Dictionary<string, string> { ["srcLang"] = "en" },
            Entries =
            [
                Entry("Active", "Acme.Orders"),
                Entry("Active", "Acme.Users")
            ]
        };

        XDocument document = await WriteDocumentAsync(catalog);
        var ids = document.Descendants(_ns + "unit").Select(unit => unit.Attribute("id")!.Value).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());

        Catalog roundTripped = Read(await WriteAsync(catalog));
        Assert.Equal(2, roundTripped.Entries.Count);
        Assert.Contains(roundTripped.Entries, entry => entry.Category == "Acme.Orders");
        Assert.Contains(roundTripped.Entries, entry => entry.Category == "Acme.Users");
        Assert.All(roundTripped.Entries, entry => Assert.Equal("Active", entry.Key));
    }

    [Fact]
    public async Task Write_TextAsKeyWithSpaces_ProducesValidNmtokenIdAndKeepsKeyInNameAsync()
    {
        // Text-as-key keys carry spaces and punctuation, none of which are valid in an NMTOKEN id. The id is
        // a hash; the raw key rides in name (the standard's "resource name").
        Catalog catalog = Categorized("Email address!", "Acme.Account");

        XDocument document = await WriteDocumentAsync(catalog);
        XElement unit = Assert.Single(document.Descendants(_ns + "unit"));
        Assert.Matches("^u[0-9a-f]{16}$", unit.Attribute("id")!.Value);
        Assert.Equal("Email address!", unit.Attribute("name")!.Value);

        Catalog roundTripped = await RoundTripAsync(catalog);
        Assert.Equal("Email address!", roundTripped.Entries[0].Key);
        Assert.Equal("Acme.Account", roundTripped.Entries[0].Category);
    }

    [Fact]
    public async Task Write_NamedCategory_WrapsUnitsInGroupNamedByCategoryAsync()
    {
        XDocument document = await WriteDocumentAsync(Categorized("save", "Acme.Labels"));

        XElement group = Assert.Single(document.Descendants(_ns + "group"));
        Assert.Equal("Acme.Labels", group.Attribute("name")!.Value);
        Assert.Matches("^g[0-9a-f]{16}$", group.Attribute("id")!.Value);
        Assert.Equal("save", Assert.Single(group.Elements(_ns + "unit")).Attribute("name")!.Value);
    }

    [Fact]
    public async Task Write_GlobalCategory_PlacesUnitsDirectlyInFileWithoutAGroupAsync()
    {
        XDocument document = await WriteDocumentAsync(Translated("greeting", "Hallo"));

        Assert.Empty(document.Descendants(_ns + "group"));
        Assert.Single(Assert.Single(document.Descendants(_ns + "file")).Elements(_ns + "unit"));
    }

    [Fact]
    public async Task Write_DuplicateIdentity_ThrowsRatherThanEmitADuplicateIdAsync()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Headers = new Dictionary<string, string> { ["srcLang"] = "en" },
            Entries = [Entry("Active", "Acme.Orders"), Entry("Active", "Acme.Orders")]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => WriteAsync(catalog));
    }

    [Fact]
    public async Task Write_UnitId_IsStableWhenOnlyTheDefaultChangesAsync()
    {
        // The id hashes the identity (category, key, context), never the default — so editing a source string
        // does not change the id and orphan its translations (design decision D-2).
        var before = await UnitIdOfAsync(Categorized("save", "Acme.Labels") with
        {
            Entries = [Categorized("save", "Acme.Labels").Entries[0] with { SourceMessage = "Save" }]
        });
        var after = await UnitIdOfAsync(Categorized("save", "Acme.Labels") with
        {
            Entries = [Categorized("save", "Acme.Labels").Entries[0] with { SourceMessage = "Save changes" }]
        });

        Assert.Equal(before, after);
    }

    private static async Task<string> UnitIdOfAsync(Catalog catalog)
    {
        XDocument document = await WriteDocumentAsync(catalog);
        return Assert.Single(document.Descendants(_ns + "unit")).Attribute("id")!.Value;
    }

    private static async Task<XDocument> WriteDocumentAsync(Catalog catalog)
    {
        using var stream = new MemoryStream(await WriteAsync(catalog));
        return XDocument.Load(stream);
    }

    private static CatalogEntry Entry(string key, string category) => new()
    {
        Key = key,
        Category = category,
        SourceMessage = key,
        TranslatedMessage = "Aktiv",
        SourceFingerprint = "fp",
        State = TranslationState.Translated
    };

    private static Catalog Categorized(string key, string category) => new()
    {
        Culture = "de",
        Headers = new Dictionary<string, string> { ["srcLang"] = "en" },
        Entries = [Entry(key, category)]
    };

    private static Catalog Translated(string key, string message) => new()
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

    [Fact]
    public void Read_DoesNotDisposeTheCallerSuppliedStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            <xliff xmlns="urn:oasis:names:tc:xliff:document:2.1" version="2.1" srcLang="en" trgLang="de">
              <file id="f1" />
            </xliff>
            """));

        _format.Read(stream);

        Assert.True(stream.CanRead);
    }

    private static async Task<Catalog> RoundTripAsync(Catalog catalog) =>
        Read(await WriteAsync(catalog));

    private static async Task<byte[]> WriteAsync(Catalog catalog, CatalogWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        await _format.WriteAsync(stream, catalog, options);
        return stream.ToArray();
    }

    private static Catalog Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return _format.Read(stream);
    }
}
