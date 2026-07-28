using System.Globalization;
using ArchPillar.Extensions.Localization.Catalogs;
using BenchmarkDotNet.Attributes;

namespace ArchPillar.Extensions.Localization.Benchmarks;

/// <summary>
/// Measures the runtime lookup hot path. A static label (literal, no arguments) should resolve with
/// zero allocations; a plural with an argument allocates only the result string.
/// </summary>
[MemoryDiagnoser]
public class LocalizerBenchmarks
{
    private const string PluralDefault = "{count, plural, one {# file} other {# files}}";
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    private CatalogStore _store = null!;
    private DefaultLocalizer _localizer = null!;
    private string _directory = null!;

    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "aplbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "de.arb"), """
            {
              "@@locale": "de",
              "app.title": "Titel",
              "@app.title": { "x-state": "Translated", "x-source-fingerprint": "b" },
              "inbox.count": "{count, plural, one {# Datei} other {# Dateien}}",
              "@inbox.count": { "x-state": "Translated", "x-source-fingerprint": "b" }
            }
            """);
        var options = new LocalizerOptions
        {
            TranslationsDirectory = _directory,
            SourceCulture = "en"
        };
        _store = new CatalogStore(options);
        _localizer = new DefaultLocalizer(_store, RenderingContext.For(options.SourceCulture, options.MissingArguments));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _store.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    // No override and no arguments: resolves to the in-code default literal (the fast path).
    [Benchmark(Baseline = true)]
    public string DefaultLiteral() => _localizer.Translate(_german, "app.ok", "OK");

    // Loaded override, literal, no arguments: also the fast path.
    [Benchmark]
    public string OverrideLiteral() => _localizer.Translate(_german, "app.title", "Title");

    // Loaded override with a plural and one argument: the dynamic path.
    [Benchmark]
    public string OverridePluralWithArgument() =>
        _localizer.Translate(_german, "inbox.count", PluralDefault, ("count", 5));
}
