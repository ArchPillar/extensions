namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The <see cref="CatalogSource"/> union on a <see cref="CatalogDescriptor"/>: a descriptor carries either a
/// <see cref="CatalogSource.Synchronous"/> or an <see cref="CatalogSource.Asynchronous"/> source, and pattern-matching
/// the union opens the right stream — the place the synchronous/asynchronous distinction lives.
/// </summary>
public sealed class CatalogSourceTests
{
    [Fact]
    public void Descriptor_CarriesSynchronousSource_PatternMatchesAndOpensTheStream()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var descriptor = new CatalogDescriptor
        {
            Culture = "de",
            Format = "arb",
            Name = "App.de.arb",
            Source = new CatalogSource.Synchronous(() => stream)
        };

        Assert.Equal(("de", "App.de.arb"), descriptor.Identity);
        Stream opened = descriptor.Source switch
        {
            CatalogSource.Synchronous sync => sync.Open(),
            CatalogSource.Asynchronous => throw new InvalidOperationException("expected synchronous"),
            _ => throw new InvalidOperationException()
        };
        Assert.Same(stream, opened);
    }

    [Fact]
    public async Task Descriptor_CarriesAsynchronousSource_PatternMatchesAndOpensTheStreamAsync()
    {
        using var stream = new MemoryStream([4, 5, 6]);
        var descriptor = new CatalogDescriptor
        {
            Culture = "fr",
            Format = "arb",
            Name = "App.fr.arb",
            Source = new CatalogSource.Asynchronous(_ => new ValueTask<Stream>(stream))
        };

        Stream opened = descriptor.Source switch
        {
            CatalogSource.Asynchronous asynchronous => await asynchronous.OpenAsync(CancellationToken.None),
            CatalogSource.Synchronous => throw new InvalidOperationException("expected asynchronous"),
            _ => throw new InvalidOperationException()
        };
        Assert.Same(stream, opened);
    }

    [Fact]
    public void Identity_DefaultsTheNameToTheEmptyStringWhenAbsent()
    {
        var descriptor = new CatalogDescriptor
        {
            Culture = "de",
            Format = "arb",
            Source = new CatalogSource.Synchronous(() => Stream.Null)
        };

        Assert.Equal(("de", string.Empty), descriptor.Identity);
    }
}
