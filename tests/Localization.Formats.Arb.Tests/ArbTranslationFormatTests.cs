using System.Text;

namespace ArchPillar.Extensions.Localization.Formats.Arb.Tests;

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
        Catalog catalog = SingleEntry("items", Plural);

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal(Plural, Assert.Single(roundTripped.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task Write_IsByteStableAcrossReadWriteCyclesAsync()
    {
        Catalog catalog = SingleEntry("greeting", "Hallo");

        var first = await WriteAsync(catalog);
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
        Catalog catalog = SingleEntry("city", "München");

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));

        Assert.Contains("\"@@locale\": \"de\"", text);
        Assert.Contains("München", text);
        Assert.DoesNotContain("\r\n", text);
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
                SourceFingerprint = "fp"
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
