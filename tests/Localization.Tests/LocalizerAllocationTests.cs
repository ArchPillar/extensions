using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class LocalizerAllocationTests : IDisposable
{
    private const int Invocations = 1000;
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    private readonly string _directory;
    private readonly CatalogStore _store;
    private readonly DefaultLocalizer _localizer;
    private readonly ILocalizer<LocalizerAllocationTests> _typed;

    public LocalizerAllocationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "aplalloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "de.arb"), """
            {
              "@@locale": "de",
              "app.title": "Titel",
              "@app.title": { "x-state": "Translated", "x-source-fingerprint": "b" }
            }
            """);
        _store = new CatalogStore(new LocalizerOptions
        {
            TranslationsDirectory = _directory,
            SourceCulture = "en"
        });
        _localizer = new DefaultLocalizer(_store);
        _typed = new LocalizerFactory(_localizer).Create<LocalizerAllocationTests>();
    }

    [Fact]
    public void TypedLocalizer_DefaultLiteral_IsAllocationFree()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = _german;
        try
        {
            AssertZeroAllocations(() =>
            {
                for (var i = 0; i < Invocations; i++)
                {
                    _typed.Translate("app.missing", "OK");
                }
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Translate_DefaultLiteral_IsAllocationFree()
    {
        AssertZeroAllocations(() =>
        {
            for (var i = 0; i < Invocations; i++)
            {
                _localizer.Translate(_german, "app.missing", "OK", context: null);
            }
        });
    }

    [Fact]
    public void Translate_OverrideLiteral_IsAllocationFree()
    {
        AssertZeroAllocations(() =>
        {
            for (var i = 0; i < Invocations; i++)
            {
                _localizer.Translate(_german, "app.title", "Title", context: null);
            }
        });
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static void AssertZeroAllocations(Action action)
    {
        // Warm-up run: populates the parse cache and triggers JIT so the measured region is steady-state.
        action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        var after = GC.GetAllocatedBytesForCurrentThread();

        var allocated = after - before;
        Assert.True(
            allocated == 0,
            $"Expected zero allocations on the lookup hot path, but {allocated} bytes were allocated over {Invocations} invocations.");
    }
}
