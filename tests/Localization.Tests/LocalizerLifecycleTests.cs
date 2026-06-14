using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// Groups every test that touches the process-wide static <see cref="Localizer"/> so they run serially:
/// they mutate shared state, so these must never run in parallel.
/// </summary>
[CollectionDefinition("Ambient", DisableParallelization = true)]
public sealed class AmbientCollection;

/// <summary>
/// The lifecycle of the static facade: <see cref="Localizer.Initialize"/> feeds the startup options and can
/// eager-load. Each test resets the static state on the way in and out so it leaves nothing behind.
/// </summary>
[Collection("Ambient")]
public sealed class LocalizerLifecycleTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    public LocalizerLifecycleTests()
    {
        Localizer.ResetAmbientForTests();
    }

    public void Dispose() => Localizer.ResetAmbientForTests();

    [Fact]
    public void Initialize_Eager_AppliesTheOptionsAndLoadsTheCatalogs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apllife-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "de.arb"), $$"""
                {
                  "@@locale": "de",
                  "@@x-category": "{{typeof(Greeting).FullName}}",
                  "hello": "Hallo",
                  "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
                }
                """);

            Localizer.Initialize(new LocalizerOptions { TranslationsDirectory = directory }, eager: true);

            WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WithCulture(CultureInfo culture, Action action)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
