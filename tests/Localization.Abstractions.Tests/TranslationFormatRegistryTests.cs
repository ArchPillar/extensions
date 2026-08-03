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
    public void ResolveByIdOrExtension_Unknown_ReturnsNull()
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

    [Fact]
    public void Equals_SameFormatSupport_AreEqual_RegardlessOfOrder()
    {
        var first = new TranslationFormatRegistry();
        first.Register(new StubFormat("arb", ".arb"));
        first.Register(new StubFormat("po", ".po"));

        var second = new TranslationFormatRegistry();
        second.Register(new StubFormat("po", ".po"));
        second.Register(new StubFormat("arb", ".arb"));

        // Distinct instances, reverse registration order — equal by format support, with equal hash codes.
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentFormatIds_AreNotEqual()
    {
        var first = new TranslationFormatRegistry();
        first.Register(new StubFormat("arb", ".arb"));

        var second = new TranslationFormatRegistry();
        second.Register(new StubFormat("po", ".po"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_SameIdDifferentFormatType_AreNotEqual()
    {
        var first = new TranslationFormatRegistry();
        first.Register(new StubFormat("arb", ".arb"));

        var second = new TranslationFormatRegistry();
        second.Register(new OtherStubFormat("arb", ".arb"));

        // Same id, different implementation type — a genuinely different format, so not equal.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_SameIdDifferentExtensions_AreNotEqual()
    {
        var first = new TranslationFormatRegistry();
        first.Register(new StubFormat("json", ".json"));

        var second = new TranslationFormatRegistry();
        second.Register(new StubFormat("json", ".jsn"));

        // Same id and type, but they resolve different extensions — so they do not offer the same format support.
        Assert.NotEqual(first, second);
    }

    private sealed class OtherStubFormat(string formatId, params string[] extensions) : ITranslationFormat
    {
        public string FormatId { get; } = formatId;

        public IReadOnlyCollection<string> Extensions { get; } = extensions;

        public FormatCapabilities Capabilities => FormatCapabilities.None;

        public Catalog Read(Stream input) =>
            throw new NotSupportedException();

        public Task WriteAsync(Stream output, Catalog catalog, CatalogWriteOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

        public Task WriteAsync(Stream output, Catalog catalog, CatalogWriteOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
