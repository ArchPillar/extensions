using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArchPillar.Extensions.Localization.AspNetCore.Tests;

/// <summary>
/// The MVC DataAnnotations integration: routing display-name and validation-message lookups through an ArchPillar
/// <see cref="ILocalizer"/> scoped to the model type. A literal with no twin is looked up as the key (the
/// text-as-key the extractor wrote); a member's <c>[Localized…]</c> twin bridges its literal to a stable key.
/// </summary>
public sealed class DataAnnotationsLocalizationTests
{
    private sealed class RegisterModel
    {
        [Display(Name = "Email address")]
        public string Email { get; set; } = "";

        [Display(Name = "Password")]
        [LocalizedDisplayName("register.password.label", "Password")]
        public string Password { get; set; } = "";
    }

    private static readonly string _category = typeof(RegisterModel).FullName!;

    [Fact]
    public void Indexer_NoTwin_LooksUpTheLiteralAsKey()
    {
        using LocalizationContext context = ContextWith("Email address", "E-Mail-Adresse");
        var localizer = new ArchPillarDataAnnotationsLocalizer(context.ForCategory(_category), typeof(RegisterModel));

        Assert.Equal("E-Mail-Adresse", InCulture("de", () => localizer["Email address"].Value));
    }

    [Fact]
    public void Indexer_Twin_BridgesTheLiteralToItsStableKey()
    {
        using LocalizationContext context = ContextWith("register.password.label", "Kennwort");
        var localizer = new ArchPillarDataAnnotationsLocalizer(context.ForCategory(_category), typeof(RegisterModel));

        Assert.Equal("Kennwort", InCulture("de", () => localizer["Password"].Value));
    }

    [Fact]
    public void Indexer_WithArguments_SubstitutesPositionalPlaceholders()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        var localizer = new ArchPillarDataAnnotationsLocalizer(context.ForCategory(_category), typeof(RegisterModel));

        Assert.Equal("Email is required", localizer["{0} is required", "Email"].Value);
    }

    [Fact]
    public void AddArchPillarDataAnnotationsLocalization_SetsTheProvider()
    {
        var services = new ServiceCollection();
        services.AddControllers().AddArchPillarDataAnnotationsLocalization();

        using ServiceProvider provider = services.BuildServiceProvider();
        MvcDataAnnotationsLocalizationOptions options =
            provider.GetRequiredService<IOptions<MvcDataAnnotationsLocalizationOptions>>().Value;

        Assert.NotNull(options.DataAnnotationLocalizerProvider);
    }

    [Fact]
    public void AddArchPillarDataAnnotationsLocalization_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IMvcBuilder)null!).AddArchPillarDataAnnotationsLocalization());

    private static LocalizationContext ContextWith(string key, string germanTranslation)
    {
        var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        context.AddCatalog(new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Category = _category,
                    Key = key,
                    SourceMessage = key,
                    TranslatedMessage = germanTranslation,
                    SourceFingerprint = "",
                    State = TranslationState.Translated,
                },
            ],
        });

        return context;
    }

    private static string InCulture(string culture, Func<string> action)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
