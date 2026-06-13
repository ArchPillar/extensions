using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat;
using Microsoft.Extensions.DependencyInjection;
using Ambient = ArchPillar.Extensions.Localization.Localizer;

namespace ArchPillar.Extensions.Localization.DependencyInjection.Tests;

public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void Localizer_IsRegisteredAsSingleton()
    {
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<DefaultLocalizer>(), provider.GetRequiredService<DefaultLocalizer>());
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

    [Fact]
    public void AmbientInterface_HonorsTheConfiguredMissingArgumentPolicy()
    {
        // The injected interface goes through the ambient store; the Throw policy configured via options must
        // apply there too, not only on a directly constructed DefaultLocalizer.
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddArchPillarLocalization(new LocalizerOptions
        {
            SourceCulture = "en",
            MissingArguments = MissingArgumentPolicy.Throw
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        ILocalizer localizer = provider.GetRequiredService<ILocalizer>();

        Assert.Throws<MissingArgumentException>(() => localizer.Translate("greet", "Hello {name}"));
    }

    [Fact]
    public void AddArchPillarLocalization_CalledTwice_IsIdempotent()
    {
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
        services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });

        // The second call is a no-op: the DefaultLocalizer marker is registered exactly once, so it does not stack a
        // second set of native views.
        Assert.Equal(1, services.Count(descriptor => descriptor.ServiceType == typeof(DefaultLocalizer)));

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ILocalizer>());
    }

    [Fact]
    public void AddArchPillarLocalization_UseAmbientFalse_IsolatesFromTheStaticAndDisablesIt()
    {
        Ambient.ResetAmbientForTests();
        try
        {
            var services = new ServiceCollection();
            services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en", UseAmbient = false });
            using ServiceProvider provider = services.BuildServiceProvider();

            // DI resolves a private, container-owned context — so injection still works...
            Assert.NotNull(provider.GetRequiredService<ILocalizer>());
            // ...while the process-wide static ambient is disabled: any static use now throws.
            Assert.Throws<InvalidOperationException>(() => Ambient.Default);
        }
        finally
        {
            Ambient.ResetAmbientForTests();
        }
    }

    public void Dispose() => Ambient.ResetAmbientForTests();

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
