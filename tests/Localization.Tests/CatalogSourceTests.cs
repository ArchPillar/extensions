namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The <see cref="CatalogSource"/> union on a <see cref="CatalogDescriptor"/>: a descriptor carries either a
/// <see cref="CatalogSource.Synchronous"/> or an <see cref="CatalogSource.Asynchronous"/> source, and pattern-matching
/// the union produces the ready <see cref="Catalog"/> — the place the synchronous/asynchronous distinction lives.
/// </summary>
public sealed class CatalogSourceTests
{
    [Fact]
    public void Descriptor_CarriesSynchronousSource_PatternMatchesAndYieldsTheCatalog()
    {
        var catalog = new Catalog { Culture = "de", Entries = [] };
        var descriptor = new CatalogDescriptor
        {
            Culture = "de",
            Format = "arb",
            Name = "App.de.arb",
            Source = new CatalogSource.Synchronous(() => catalog)
        };

        Assert.Equal(("de", "App.de.arb"), descriptor.Identity);
        Catalog produced = descriptor.Source switch
        {
            CatalogSource.Synchronous sync => sync.Open(),
            CatalogSource.Asynchronous => throw new InvalidOperationException("expected synchronous"),
            _ => throw new InvalidOperationException()
        };
        Assert.Same(catalog, produced);
    }

    [Fact]
    public async Task Descriptor_CarriesAsynchronousSource_PatternMatchesAndYieldsTheCatalogAsync()
    {
        var catalog = new Catalog { Culture = "fr", Entries = [] };
        var descriptor = new CatalogDescriptor
        {
            Culture = "fr",
            Format = "arb",
            Name = "App.fr.arb",
            Source = new CatalogSource.Asynchronous(_ => new ValueTask<Catalog>(catalog))
        };

        Catalog produced = descriptor.Source switch
        {
            CatalogSource.Asynchronous asynchronous => await asynchronous.OpenAsync(CancellationToken.None),
            CatalogSource.Synchronous => throw new InvalidOperationException("expected asynchronous"),
            _ => throw new InvalidOperationException()
        };
        Assert.Same(catalog, produced);
    }

    [Fact]
    public void Identity_DefaultsTheNameToTheEmptyStringWhenAbsent()
    {
        var descriptor = new CatalogDescriptor
        {
            Culture = "de",
            Format = "arb",
            Source = new CatalogSource.Synchronous(() => new Catalog { Culture = "de", Entries = [] })
        };

        Assert.Equal(("de", string.Empty), descriptor.Identity);
    }
}
