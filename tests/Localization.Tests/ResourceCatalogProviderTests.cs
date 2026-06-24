using System.Globalization;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The resource catalog provider: <see cref="ICatalogProvider.Catalogs"/> lists the embedded
/// [LocalizationCatalog] catalogs across the loaded assemblies, and <see cref="ICatalogProvider.CatalogsFor"/>
/// additionally probes each satellite-marked assembly for the culture. The test assembly declares both an
/// embedded German catalog and a German satellite, so the provider is exercised against a real assembly. Every
/// descriptor's source is <see cref="CatalogSource.Synchronous"/>; all reflection is guarded.
/// </summary>
public sealed class ResourceCatalogProviderTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void Catalogs_ReturnsTheEmbeddedCatalog()
    {
        var provider = new ResourceCatalogProvider();

        CatalogDescriptor embedded = Assert.Single(provider.Catalogs, descriptor => descriptor.Name == "embedded.de.arb");

        Assert.Equal("de", embedded.Culture);
        Assert.Equal("arb", embedded.Format);
        Assert.IsType<CatalogSource.Synchronous>(embedded.Source);
    }

    [Fact]
    public void Catalogs_DoesNotProbeSatellites()
    {
        var provider = new ResourceCatalogProvider();

        IReadOnlyList<CatalogDescriptor> descriptors = provider.Catalogs;

        // The satellite catalog (sat.key) is only reachable by probing a culture, never by listing.
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Name?.Contains("satellite", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void SynchronousSource_OpensTheEmbeddedResourceBytes()
    {
        var provider = new ResourceCatalogProvider();
        CatalogDescriptor embedded = Assert.Single(provider.Catalogs, descriptor => descriptor.Name == "embedded.de.arb");
        CatalogSource.Synchronous sync = Assert.IsType<CatalogSource.Synchronous>(embedded.Source);

        using Stream stream = sync.Open();
        Catalog catalog = new ArbTranslationFormat().Read(stream);

        Assert.Contains(catalog.Entries, entry => entry.TranslatedMessage == "Eingebettet");
    }

    [Fact]
    public void CatalogsFor_ProbesTheSatelliteForTheCulture()
    {
        var provider = new ResourceCatalogProvider();

        IReadOnlyList<CatalogDescriptor> german = provider.CatalogsFor(_german);

        // Both the embedded German catalog and the German satellite catalog are present.
        Assert.Contains(german, descriptor => descriptor.Name == "embedded.de.arb");
        CatalogDescriptor satellite = Assert.Single(german, descriptor => descriptor.Culture == "de" && descriptor.Name != "embedded.de.arb");
        CatalogSource.Synchronous sync = Assert.IsType<CatalogSource.Synchronous>(satellite.Source);

        using Stream stream = sync.Open();
        Catalog catalog = new ArbTranslationFormat().Read(stream);
        Assert.Contains(catalog.Entries, entry => entry.TranslatedMessage == "Aus dem Satelliten");
    }

    [Fact]
    public void CatalogsFor_CultureWithoutASatelliteReturnsOnlyEmbeddedMatches()
    {
        var provider = new ResourceCatalogProvider();

        // No 'fr' satellite and no embedded 'fr' catalog ships, so the result holds neither.
        IReadOnlyList<CatalogDescriptor> french = provider.CatalogsFor(_french);

        Assert.DoesNotContain(french, descriptor => string.Equals(descriptor.Culture, "fr", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Watch_ReturnsADisposableThatUnsubscribesCleanly()
    {
        var provider = new ResourceCatalogProvider();

        IDisposable handle = provider.Watch(_ => { });

        Assert.NotNull(handle);
        handle.Dispose();
    }
}
