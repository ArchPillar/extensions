using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Ambient = ArchPillar.Extensions.Localization.Localizer;

namespace ArchPillar.Extensions.Localization.StringLocalizer.Tests;

public sealed class StringLocalizerServiceCollectionExtensionsTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    private readonly string _directory;

    public StringLocalizerServiceCollectionExtensionsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "aplsl-" + Guid.NewGuid().ToString("N"));
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

        Assert.NotNull(provider.GetRequiredService<IStringLocalizer<StringLocalizerServiceCollectionExtensionsTests>>());
    }

    [Fact]
    public void StringLocalizer_ComposesOverAPreviouslyRegisteredFactory()
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
                    Key = "ambient",
                    SourceMessage = "Ambient",
                    TranslatedMessage = "AmbientDe",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        });

        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizerFactory>(new FakeFactory());
        services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
        using ServiceProvider provider = services.BuildServiceProvider();

        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizerFactory>().Create(typeof(Buttons));

        WithCulture(_german, () =>
        {
            // The ambient hit wins.
            Assert.Equal("AmbientDe", localizer["ambient"].Value);

            // The ambient misses, so it falls through to the previously-registered factory.
            LocalizedString fromInner = localizer["inner"];
            Assert.Equal("FromInner", fromInner.Value);
            Assert.False(fromInner.ResourceNotFound);

            // Neither has it, so the name (the in-code default) is returned, flagged not-found.
            LocalizedString miss = localizer["missing"];
            Assert.Equal("missing", miss.Value);
            Assert.True(miss.ResourceNotFound);
        });
    }

    [Fact]
    public void GetAllStrings_IncludesAmbientEntriesForTheCategory()
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
        services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
        using ServiceProvider provider = services.BuildServiceProvider();
        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizerFactory>().Create(typeof(Buttons));

        WithCulture(_german, () =>
        {
            List<LocalizedString> all = [.. localizer.GetAllStrings(includeParentCultures: false)];
            Assert.Contains(all, s => s.Name == "save" && s.Value == "Speichern");
        });
    }

    [Fact]
    public void AddLocalizationAfterUs_StillRegistersTheResourceManagerFactory()
    {
        // Reverse of the documented order: the host calls AddLocalization after us. Its TryAdd would be
        // suppressed by our composing factory, silently dropping all .resx — so we register the ResourceManager
        // factory ourselves, and it must be present regardless of order (Decision D-F3).
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
        services.AddLocalization();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStringLocalizerFactory)
            && descriptor.ImplementationType == typeof(ResourceManagerStringLocalizerFactory));
    }

    [Fact]
    public void AddArchPillarStringLocalizer_CalledTwice_IsIdempotent()
    {
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
        services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });

        // The second call is a no-op (the native marker and the interop marker are each registered exactly
        // once), so it does not stack a second composing factory over the first.
        Assert.Equal(1, services.Count(descriptor => descriptor.ServiceType == typeof(DefaultLocalizer)));
        Assert.Equal(1, services.Count(descriptor =>
            descriptor.ServiceType == typeof(IStringLocalizerFactory)
            && descriptor.ImplementationFactory is not null));

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IStringLocalizer>());
    }

    [Fact]
    public void StringLocalizer_NameWithCompositeFormat_DoesNotThrowAndIsNotIcuFormatted()
    {
        // A ResourceManager-style name like "Price: {0:C}" is not ICU; the adapter must not run it through the
        // ICU formatter on a miss. With no inner factory it returns the name with string.Format applied.
        using ServiceProvider provider = BuildProvider();
        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizer>();

        LocalizedString result = localizer["Price: {0:C}", 9.99m];

        Assert.True(result.ResourceNotFound);
        Assert.DoesNotContain("{0:C}", result.Value);
    }

    [Fact]
    public void StringLocalizer_NameWithCompositeFormat_FallsThroughToInnerFactory()
    {
        // The migration case: the value lives in the existing .resx factory. The adapter must reach it, not
        // throw on the ICU-incompatible name first.
        Ambient.Reset();
        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizerFactory>(new FakeFactory());
        services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
        using ServiceProvider provider = services.BuildServiceProvider();

        IStringLocalizer localizer = provider.GetRequiredService<IStringLocalizerFactory>().Create(typeof(Buttons));
        LocalizedString result = localizer["Price: {0:C}", 9.99m];

        Assert.False(result.ResourceNotFound);
        Assert.Contains("9.99", result.Value);
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
        services.AddArchPillarStringLocalizer(new LocalizerOptions { TranslationsDirectory = _directory, SourceCulture = "en" });
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

    // A stand-in for a previously-registered factory (such as ResourceManager): it knows exactly one key,
    // "inner", and reports every other key as not found so composition and fall-through can be observed.
    private sealed class FakeFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource) => new FakeLocalizer();

        public IStringLocalizer Create(string baseName, string location) => new FakeLocalizer();
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => name == "inner"
            ? new LocalizedString(name, "FromInner", resourceNotFound: false)
            : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            name.StartsWith("Price:", StringComparison.Ordinal)
                ? new LocalizedString(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: false)
                : this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
