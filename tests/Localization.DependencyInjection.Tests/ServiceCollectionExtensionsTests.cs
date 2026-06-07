using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Ambient = ArchPillar.Extensions.Localization.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection.Tests;

public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    private readonly string _directory;

    public ServiceCollectionExtensionsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "apldi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "de.arb"), """
            {
              "@@locale": "de",
              "home.greeting": "Hallo",
              "@home.greeting": { "x-state": "Translated", "x-source-fingerprint": "fp" },
              "inbox.count": "Sie haben {0}",
              "@inbox.count": { "x-state": "Translated", "x-source-fingerprint": "fp" }
            }
            """);
    }

    [Fact]
    public void StringLocalizer_ResolvesOverrideForCurrentCulture()
    {
        using ServiceProvider provider = BuildProvider();
        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizer>();

        WithCulture(_german, () => Assert.Equal("Hallo", localizer["home.greeting"].Value));
    }

    [Fact]
    public void StringLocalizer_MissingKey_ReturnsTheName()
    {
        using ServiceProvider provider = BuildProvider();
        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizer>();

        WithCulture(_german, () => Assert.Equal("does.not.exist", localizer["does.not.exist"].Value));
    }

    [Fact]
    public void StringLocalizer_FormatsPositionalArguments()
    {
        using ServiceProvider provider = BuildProvider();
        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizer>();

        WithCulture(_german, () => Assert.Equal("Sie haben 5", localizer["inbox.count", 5].Value));
    }

    [Fact]
    public void GenericStringLocalizer_IsResolvable()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IStringLocalizer<ServiceCollectionExtensionsTests>>());
    }

    [Fact]
    public void Localizer_IsRegisteredAsSingleton()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<Localizer>(), provider.GetRequiredService<Localizer>());
    }

    [Fact]
    public void TypedLocalizer_ReadsTheAmbientStore()
    {
        Ambient.Reset();
        Ambient.AddCatalog(new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Category = typeof(Buttons).FullName!,
                    Key = "save",
                    SourceMessage = "Save",
                    TranslatedMessage = "Speichern",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        });

        var services = new ServiceCollection();
        services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
        using ServiceProvider provider = services.BuildServiceProvider();

        ILocalizer<Buttons> localizer = provider.GetRequiredService<ILocalizer<Buttons>>();
        WithCulture(_german, () => Assert.Equal("Speichern", localizer.Translate("save", "Save")));
    }

    public void Dispose()
    {
        Ambient.Reset();
        Directory.Delete(_directory, recursive: true);
    }

    private ServiceProvider BuildProvider()
    {
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddArchPillarLocalization(new LocalizerOptions { TranslationsDirectory = _directory, SourceCulture = "en" });
        return services.BuildServiceProvider();
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

    private sealed class Buttons;
}
