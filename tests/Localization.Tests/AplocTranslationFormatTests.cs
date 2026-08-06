using System.Text;
using System.Text.Json;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class AplocTranslationFormatTests
{
    private static readonly AplocTranslationFormat _format = new();

    [Fact]
    public void Metadata_DescribesAploc()
    {
        Assert.Equal("aploc", _format.FormatId);
        Assert.Contains(".aploc", _format.Extensions);
        Assert.True(_format.Capabilities.HasFlag(FormatCapabilities.IcuPlural));
    }

    [Fact]
    public async Task RoundTrip_PreservesCultureCategoryKeyAndValueAsync()
    {
        var catalog = new Catalog
        {
            Culture = "da",
            Entries =
            [
                Entry("Back", "Acme.Widgets.ButtonLabels", "Tilbage"),
                Entry("Cancel", "Acme.Widgets.ButtonLabels", "Annuller"),
                Entry("AllDay", "Acme.Widgets.CalendarLabels", "Hele dagen"),
                Entry("Large", "Acme.Models.Size", "Stor")
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        Assert.Equal("da", roundTripped.Culture);
        Assert.Equal(4, roundTripped.Entries.Count);
        AssertEntry(roundTripped, "Acme.Widgets.ButtonLabels", "Back", "Tilbage");
        AssertEntry(roundTripped, "Acme.Widgets.ButtonLabels", "Cancel", "Annuller");
        AssertEntry(roundTripped, "Acme.Widgets.CalendarLabels", "AllDay", "Hele dagen");
        AssertEntry(roundTripped, "Acme.Models.Size", "Large", "Stor");
        // A deploy read is a complete translation: the value fills the source fields the format does not store.
        CatalogEntry back = Find(roundTripped, "Acme.Widgets.ButtonLabels", "Back");
        Assert.Equal("Tilbage", back.TranslatedMessage);
        Assert.Equal(TranslationState.Translated, back.State);
    }

    [Fact]
    public async Task Write_FoldsSharedNamespaceSegmentsIntoOneNestedObjectAsync()
    {
        var catalog = new Catalog
        {
            Culture = "da",
            Entries =
            [
                Entry("Back", "Acme.Widgets.ButtonLabels", "Tilbage"),
                Entry("AllDay", "Acme.Widgets.CalendarLabels", "Hele dagen"),
                Entry("Large", "Acme.Models.Size", "Stor")
            ]
        };

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));

        // The shared "Acme" segment is written once; under it, "Widgets" and "Models" are sibling objects, and a
        // node's own keys are plain string members of the leaf namespace — no "@" in a real bundle.
        Assert.Equal(1, CountOccurrences(text, "\"Acme\""));
        Assert.DoesNotContain("\"@\"", text);
        using var doc = JsonDocument.Parse(text);
        JsonElement acme = doc.RootElement.GetProperty("Acme");
        JsonElement widgets = acme.GetProperty("Widgets");
        Assert.Equal("Tilbage", widgets.GetProperty("ButtonLabels").GetProperty("Back").GetString());
        Assert.Equal("Stor", acme.GetProperty("Models").GetProperty("Size").GetProperty("Large").GetString());
        Assert.Equal("da", doc.RootElement.GetProperty("@@locale").GetString());
    }

    [Fact]
    public async Task RoundTrip_PlacesUncategorizedEntriesDirectlyAtTheRootAsync()
    {
        Catalog catalog = SingleEntry("greeting", "Hej", category: "");

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));
        using (var doc = JsonDocument.Parse(text))
        {
            // Uncategorized entries are the root node's own plain string members, sitting directly at the root.
            Assert.Equal("Hej", doc.RootElement.GetProperty("greeting").GetString());
        }

        CatalogEntry entry = Assert.Single((await RoundTripAsync(catalog)).Entries);
        Assert.Equal("", entry.Category);
        Assert.Equal("greeting", entry.Key);
        Assert.Equal("Hej", entry.TranslatedMessage);
    }

    [Fact]
    public async Task RoundTrip_TreatsADottedKeyAsOneLeafNotNestedSegmentsAsync()
    {
        // Only the category nests; a key is a leaf member taken verbatim. A dotted key (home.title) or a dotted
        // key in a category must round-trip as one key, never split into namespace objects.
        var catalog = new Catalog
        {
            Culture = "da",
            Entries =
            [
                Entry("home.title", "", "Forside"),
                Entry("inbox.summary", "Acme.Widgets.Pages.Home", "Indbakke")
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        AssertEntry(roundTripped, "", "home.title", "Forside");
        AssertEntry(roundTripped, "Acme.Widgets.Pages.Home", "inbox.summary", "Indbakke");
    }

    [Fact]
    public async Task RoundTrip_PreservesIcuPluralVerbatimAsync()
    {
        const string Plural = "{count, plural, one {# dag} other {# dage}}";

        Catalog roundTripped = await RoundTripAsync(SingleEntry("Days", Plural, "Acme.Widgets.TimeLabels"));

        Assert.Equal(Plural, Assert.Single(roundTripped.Entries).TranslatedMessage);
    }

    [Theory]
    [InlineData("a line\nwith a newline")]
    [InlineData("has an = equals and {braces} and \"quotes\"")]
    [InlineData("unicode: æøå smørrebrød")]
    [InlineData("a backslash \\ and a tab \t")]
    public async Task RoundTrip_KeepsAwkwardValuesIntactViaJsonEscapingAsync(string value)
    {
        Catalog roundTripped = await RoundTripAsync(SingleEntry("key", value, "Acme.Widgets.ButtonLabels"));

        Assert.Equal(value, Assert.Single(roundTripped.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task Write_MinifiedHasNoInsignificantWhitespaceAsync()
    {
        Catalog catalog = SingleEntry("Back", "Tilbage", "Acme.Widgets.ButtonLabels");

        var minified = Encoding.UTF8.GetString(await WriteAsync(catalog, new CatalogWriteOptions { Minify = true }));
        var pretty = Encoding.UTF8.GetString(await WriteAsync(catalog, CatalogWriteOptions.Default));

        Assert.DoesNotContain('\n', minified);
        Assert.Contains('\n', pretty);
        // Both describe the same catalog.
        Assert.Equal("Tilbage", Find(Read(Encoding.UTF8.GetBytes(minified)), "Acme.Widgets.ButtonLabels", "Back").TranslatedMessage);
    }

    [Fact]
    public async Task Write_IsByteStableAcrossReadWriteCyclesAsync()
    {
        var catalog = new Catalog
        {
            Culture = "da",
            Entries =
            [
                Entry("Cancel", "Acme.Widgets.ButtonLabels", "Annuller"),
                Entry("Back", "Acme.Widgets.ButtonLabels", "Tilbage"),
                Entry("Large", "Acme.Models.Size", "Stor")
            ]
        };

        var first = await WriteAsync(catalog);
        var second = await WriteAsync(Read(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RoundTrip_KeyEqualToASiblingNamespaceSegment_BecomesThatNamespacesApexAsync()
    {
        // The one case that needs the apex: a key "Size" in category Acme.Models has the same name as the deeper
        // category Acme.Models.Size. The key becomes that namespace object's "@" member — the record at the node
        // itself — so the segment name is written once and both survive.
        var catalog = new Catalog
        {
            Culture = "da",
            Entries =
            [
                Entry("Size", "Acme.Models", "Størrelse"),
                Entry("Large", "Acme.Models.Size", "Stor")
            ]
        };

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));
        using (var doc = JsonDocument.Parse(text))
        {
            JsonElement size = doc.RootElement.GetProperty("Acme").GetProperty("Models").GetProperty("Size");
            Assert.Equal("Størrelse", size.GetProperty("@").GetString());
            Assert.Equal("Stor", size.GetProperty("Large").GetString());
            // The segment is written once — as the namespace object, not also as a displaced sibling key.
            Assert.Equal(1, CountOccurrences(text, "\"Size\""));
        }

        Catalog roundTripped = Read(Encoding.UTF8.GetBytes(text));
        AssertEntry(roundTripped, "Acme.Models", "Size", "Størrelse");
        AssertEntry(roundTripped, "Acme.Models.Size", "Large", "Stor");
    }

    [Fact]
    public async Task RoundTrip_RepeatedSegmentCategories_AcmeAcme_And_AcmeAcmeAcme_Async()
    {
        // Repeated segments and a prefix relationship: a key "Acme" in category Acme.Acme has the same name as the
        // deeper category Acme.Acme.Acme, so it becomes that namespace's apex; the unambiguous key "K" stays a
        // plain member. Every level is its own node, so it holds.
        var catalog = new Catalog
        {
            Culture = "da",
            Entries =
            [
                Entry("Acme", "Acme.Acme", "one"),
                Entry("K", "Acme.Acme.Acme", "two")
            ]
        };

        Catalog roundTripped = await RoundTripAsync(catalog);

        AssertEntry(roundTripped, "Acme.Acme", "Acme", "one");
        AssertEntry(roundTripped, "Acme.Acme.Acme", "K", "two");
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@@locale")]
    [InlineData("@@custom")]
    public async Task Write_ThrowsForAReservedKeyNameAsync(string key)
    {
        // "@" names a namespace's own value and "@@" prefixes the file headers, so neither is a usable key. The
        // writer rejects them by name rather than emitting a bundle that would read back as something else.
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => WriteAsync(SingleEntry(key, "Hej", "Acme.Widgets")));

        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RoundTrip_KeyStartingWithASingleAtIsAnOrdinaryKeyAsync()
    {
        // Only a bare "@" and the "@@" header prefix are reserved; a key like "@id" is unambiguous, so it stays a
        // plain member with no special handling.
        Catalog catalog = SingleEntry("@id", "Identitet", "Acme.Widgets");

        var text = Encoding.UTF8.GetString(await WriteAsync(catalog));
        using (var doc = JsonDocument.Parse(text))
        {
            Assert.Equal("Identitet", doc.RootElement.GetProperty("Acme").GetProperty("Widgets").GetProperty("@id").GetString());
        }

        AssertEntry(Read(Encoding.UTF8.GetBytes(text)), "Acme.Widgets", "@id", "Identitet");
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

    private static Catalog SingleEntry(string key, string message, string category) => new()
    {
        Culture = "da",
        Entries = [Entry(key, category, message)]
    };

    private static void AssertEntry(Catalog catalog, string category, string key, string expected) =>
        Assert.Equal(expected, Find(catalog, category, key).TranslatedMessage);

    private static CatalogEntry Find(Catalog catalog, string category, string key) =>
        catalog.Entries.Single(e => e.Category == category && e.Key == key);

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static async Task<Catalog> RoundTripAsync(Catalog catalog) => Read(await WriteAsync(catalog));

    private static async Task<byte[]> WriteAsync(Catalog catalog) => await WriteAsync(catalog, CatalogWriteOptions.Default);

    private static async Task<byte[]> WriteAsync(Catalog catalog, CatalogWriteOptions options)
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
