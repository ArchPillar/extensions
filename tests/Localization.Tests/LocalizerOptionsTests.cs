using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// <see cref="LocalizerOptions"/> value equality. The record holds reference-type members — the culture and
/// provider lists and the format registry — so its equality is written by hand to compare them by content. Two
/// independently built but equivalent options must therefore compare equal (so the configure-once guard can dedupe
/// them), while any genuine difference must not.
/// </summary>
public sealed class LocalizerOptionsTests
{
    [Fact]
    public void Defaults_AreEqual_WithEqualHashCodes()
    {
        var first = new LocalizerOptions();
        var second = new LocalizerOptions();

        // Distinct instances (distinct default Formats registries and Providers), equal by value.
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void DifferentScalarOption_NotEqual()
    {
        var options = new LocalizerOptions { SourceCulture = "en" };

        Assert.NotEqual(options, options with { SourceCulture = "de" });
        Assert.NotEqual(options, options with { EnableHotReload = true });
        Assert.NotEqual(options, options with { HotReloadDebounce = TimeSpan.FromSeconds(1) });
    }

    [Fact]
    public void Cultures_ComparedByContent()
    {
        var listed = new LocalizerOptions { Cultures = ["de", "fr"] };

        Assert.Equal(listed, new LocalizerOptions { Cultures = ["de", "fr"] });
        Assert.NotEqual(listed, new LocalizerOptions { Cultures = ["de", "es"] });
        Assert.NotEqual(listed, new LocalizerOptions { Cultures = ["de"] });
        Assert.NotEqual(listed, new LocalizerOptions()); // null (discover all) vs a list
    }

    [Fact]
    public void Providers_ComparedByContent()
    {
        Func<LocalizerOptions, ICatalogProvider> factory = EmptyProviderFactory();

        // Lists holding the same factory instance are equal; a different length (missing or extra provider) is not.
        Assert.Equal(
            new LocalizerOptions { Providers = [factory] },
            new LocalizerOptions { Providers = [factory] });
        Assert.NotEqual(
            new LocalizerOptions { Providers = [factory] },
            new LocalizerOptions());
        Assert.NotEqual(
            new LocalizerOptions { Providers = [factory] },
            new LocalizerOptions { Providers = [factory, factory] });
    }

    [Fact]
    public void Formats_ComparedByFormatSupport()
    {
        TranslationFormatRegistry custom = BuiltInTranslationFormats.CreateRegistry();
        custom.Register(new StubFormat("custom", ".custom"));

        // Two default registries support the same formats; the custom one adds an id, so it differs.
        Assert.Equal(new LocalizerOptions(), new LocalizerOptions());
        Assert.NotEqual(new LocalizerOptions(), new LocalizerOptions { Formats = custom });
    }

    // A factory delegate over an empty in-memory provider, so a test can layer a provider without a real catalog.
    private static Func<LocalizerOptions, ICatalogProvider> EmptyProviderFactory() =>
        _ => new InMemoryCatalogProvider([]);

    private sealed class StubFormat(string formatId, params string[] extensions) : ITranslationFormat
    {
        public string FormatId { get; } = formatId;

        public IReadOnlyCollection<string> Extensions { get; } = extensions;

        public FormatCapabilities Capabilities => FormatCapabilities.None;

        public Catalog Read(Stream input) =>
            throw new NotSupportedException();

        public Task WriteAsync(Stream output, Catalog catalog, CatalogWriteOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
