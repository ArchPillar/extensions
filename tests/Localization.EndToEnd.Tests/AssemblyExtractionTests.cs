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
        using static ArchPillar.Extensions.Localization.TranslationMarkers;

        namespace App;

        public sealed class Home;

        // A user-defined indexer: the library ships none, but the attribute contract recognises any shape,
        // so a consumer's own indexer is extracted exactly like Translate — category from [TranslationScope].
        public interface ICustomStrings<[TranslationScope] T>
        {
            string this[[Translatable] string key, [TranslationDefault] string message] { get; }
        }

        public sealed class Consumer
        {
            public void Run(ILocalizer<Home> loc, IStringLocalizer<Home> strings, ICustomStrings<Home> custom)
            {
                loc.Translate("home.title", "Inbox");                                            // empty params -> Array.Empty
                loc.Translate("inbox.count", "{count, plural, other {# msgs}}", ("count", 3));    // tuple arg -> newobj
                _ = strings["inbox.summary", 3];                                                 // IStringLocalizer indexer
                _ = custom["greeting", "Hello"];                                                 // a user-defined indexer
                _ = Localizer.Translate("tagline", "Welcome");                                // static using-static form, global
                L("Email is required");                                                          // L(...) marker, global category
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
        var assembly = GeneratorPipeline.EmitAssembly(ConsumerCode, "ExtractTarget", _directory);

        using var extractor = new AssemblyStringExtractor();
        IReadOnlyList<RawCallSite> sites = extractor.Extract(assembly, includeAnnotations: false).CallSites;

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

        // A consumer's own indexer — recognised by the same [Translatable] / [TranslationDefault] attribute
        // contract as Translate, not a hardcoded name, with the category from its [TranslationScope] argument.
        RawCallSite greeting = Assert.Single(sites, s => s.Key == "greeting");
        Assert.Equal("Hello", greeting.Default);
        Assert.Equal("App.Home", greeting.Category);

        // The static Localizer.Translate (the using-static free-function form): recognised by the same
        // attribute contract, under the global category (a receiver-less static call).
        RawCallSite tagline = Assert.Single(sites, s => s.Key == "tagline");
        Assert.Equal("Welcome", tagline.Default);
        Assert.Equal(string.Empty, tagline.Category);

        // The L(...) marker: its single parameter carries both attributes, so the literal is key and default,
        // under the global category.
        RawCallSite marker = Assert.Single(sites, s => s.Key == "Email is required");
        Assert.Equal("Email is required", marker.Default);
        Assert.Equal(string.Empty, marker.Category);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
