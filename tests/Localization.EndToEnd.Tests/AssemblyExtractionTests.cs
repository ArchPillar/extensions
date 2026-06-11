using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// The Decision D-K engine: extraction reads the built assembly's IL, so it covers call sites the source
/// generator never sees (Razor/<c>.cshtml</c>). These cases reproduce the exact IL shapes Razor compiles to —
/// an empty <c>params</c> (lowered to <c>Array.Empty</c>), a tuple argument (lowered to <c>newobj</c>), and
/// the <c>IStringLocalizer</c> indexer — and confirm the IL reader recovers the key, default, and category.
/// </summary>
public sealed class AssemblyExtractionTests : IDisposable
{
    private const string ConsumerCode = """
        using ArchPillar.Extensions.Localization;
        using Microsoft.Extensions.Localization;

        namespace App;

        public sealed class Home;

        public sealed class Consumer
        {
            public void Run(ILocalizer<Home> loc, IStringLocalizer<Home> strings)
            {
                loc.Translate("home.title", "Inbox");                                            // empty params -> Array.Empty
                loc.Translate("inbox.count", "{count, plural, other {# msgs}}", ("count", 3));    // tuple arg -> newobj
                _ = strings["inbox.summary", 3];                                                 // indexer
            }
        }
        """;

    private readonly string _directory;

    public AssemblyExtractionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "apl-il-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Extract_RecoversCallSitesFromIl_IncludingTheShapesRazorProduces()
    {
        var assembly = GeneratorPipeline.EmitAssemblyWithTemplate(ConsumerCode, "ExtractTarget", _directory);

        IReadOnlyList<RawCallSite> sites = AssemblyStringExtractor.Extract(assembly);

        // Translate with an empty params list (the Array.Empty shape that defeated a naive scan).
        RawCallSite title = Assert.Single(sites, s => s.Key == "home.title");
        Assert.Equal("Inbox", title.Default);
        Assert.Equal("App.Home", title.Category);

        // Translate with a tuple argument (the newobj shape).
        RawCallSite count = Assert.Single(sites, s => s.Key == "inbox.count");
        Assert.Equal("{count, plural, other {# msgs}}", count.Default);
        Assert.Equal("App.Home", count.Category);

        // The IStringLocalizer indexer: the name is both key and default, category from the type argument.
        RawCallSite summary = Assert.Single(sites, s => s.Key == "inbox.summary");
        Assert.Equal("inbox.summary", summary.Default);
        Assert.Equal("App.Home", summary.Category);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
