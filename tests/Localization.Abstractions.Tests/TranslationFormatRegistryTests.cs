namespace ArchPillar.Extensions.Localization.Abstractions.Tests;

public sealed class TranslationFormatRegistryTests
{
    [Fact]
    public void ResolveById_ReturnsRegisteredFormat()
    {
        var registry = new TranslationFormatRegistry();
        var arb = new StubFormat("arb", ".arb");
        registry.Register(arb);

        Assert.Same(arb, registry.ResolveById("arb"));
        Assert.Same(arb, registry.ResolveById("ARB"));
    }

    [Fact]
    public void ResolveByExtension_NormalizesLeadingDot()
    {
        var registry = new TranslationFormatRegistry();
        var xliff = new StubFormat("xliff", ".xliff", ".xlf");
        registry.Register(xliff);

        Assert.Same(xliff, registry.ResolveByExtension(".xliff"));
        Assert.Same(xliff, registry.ResolveByExtension("xlf"));
        Assert.Same(xliff, registry.ResolveByExtension(".XLF"));
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNull()
    {
        var registry = new TranslationFormatRegistry();

        Assert.Null(registry.ResolveById("po"));
        Assert.Null(registry.ResolveByExtension(".po"));
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var registry = new TranslationFormatRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    private sealed class StubFormat : ITranslationFormat
    {
        public StubFormat(string formatId, params string[] extensions)
        {
            FormatId = formatId;
            Extensions = extensions;
        }

        public string FormatId { get; }

        public IReadOnlyCollection<string> Extensions { get; }

        public FormatCapabilities Capabilities => FormatCapabilities.None;

        public Catalog Read(Stream input) =>
            throw new NotSupportedException();

        public Task WriteAsync(Stream output, Catalog catalog, CancellationToken cancellationToken, CatalogWriteOptions? options = null) =>
            throw new NotSupportedException();
    }
}
