using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The runtime counterpart of the build-time annotation extraction: <see cref="EnumLocalizationExtensions"/>
/// reads an enum member's display annotation by reflection and resolves it through the localizer under the enum
/// type's category, mirroring the (category, key, default) the extractor wrote — a <c>[Localized…]</c> twin's
/// stable key when present, otherwise the system attribute's literal.
/// </summary>
public sealed class EnumLocalizationExtensionsTests
{
    private enum OrderStatus
    {
        [Display(Name = "Active")]
        Active,

        [Display(Name = "order.status.pending")]
        [LocalizedDisplayName("Pending review")]
        Pending,

        Unlabelled,
    }

    [Fact]
    public void GetLocalizedDisplayName_DisplayAttribute_NoOverride_ReturnsSourceDefault()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        Assert.Equal("Active", OrderStatus.Active.GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_TwinDefault_NoOverride_ReturnsTheTwinDefaultNotTheKey()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        Assert.Equal("Pending review", OrderStatus.Pending.GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_NoAnnotation_ReturnsTheMemberName()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        Assert.Equal("Unlabelled", OrderStatus.Unlabelled.GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_LocalizedDisplayNameTwin_ResolvesOverrideUnderStableKey()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        context.AddCatalog(new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Category = typeof(OrderStatus).FullName!,
                    Key = "order.status.pending",
                    SourceMessage = "Pending review",
                    TranslatedMessage = "Zur Prüfung",
                    SourceFingerprint = "",
                    State = TranslationState.Translated,
                },
            ],
        });

        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Zur Prüfung", OrderStatus.Pending.GetLocalizedDisplayName(context));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
