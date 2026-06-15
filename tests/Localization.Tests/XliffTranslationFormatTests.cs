using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class XliffTranslationFormatTests
{
    private static readonly XliffTranslationFormat _format = new();

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
        var second = await WriteAsync(await ReadAsync(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Read_ParsesXliff21Async()
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

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Xliff));

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
    public async Task Read_Xliff20_ParsesInsteadOfReturningEmptyAsync()
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

        Catalog catalog = await ReadAsync(Encoding.UTF8.GetBytes(Xliff));

        CatalogEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("Hello", entry.SourceMessage);
        Assert.Equal("Hallo", entry.TranslatedMessage);
    }

    [Fact]
    public async Task Read_Xliff12_ThrowsRatherThanReturnEmptyAsync()
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

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await ReadAsync(Encoding.UTF8.GetBytes(Xliff)));
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

    private static async Task<Catalog> RoundTripAsync(Catalog catalog) =>
        await ReadAsync(await WriteAsync(catalog));

    private static async Task<byte[]> WriteAsync(Catalog catalog, CatalogWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        await _format.WriteAsync(stream, catalog, CancellationToken.None, options);
        return stream.ToArray();
    }

    private static async Task<Catalog> ReadAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await _format.ReadAsync(stream, CancellationToken.None);
    }
}
