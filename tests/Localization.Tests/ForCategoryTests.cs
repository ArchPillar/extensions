using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// <see cref="LocalizationContext.ForCategory(string)"/> is the dynamic-category parallel of
/// <see cref="LocalizationContext.For{T}"/>: a localizer scoped to a category computed at runtime (a model
/// type's name, say), for callers that have the category as a string rather than a type argument.
/// </summary>
public sealed class ForCategoryTests
{
    [Fact]
    public void ForCategory_ResolvesOverrideUnderTheGivenCategory()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        context.AddCatalog(new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Category = "Shop.Cart",
                    Key = "title",
                    SourceMessage = "Cart",
                    TranslatedMessage = "Warenkorb",
                    SourceFingerprint = "",
                    State = TranslationState.Translated,
                },
            ],
        });

        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Warenkorb", context.ForCategory("Shop.Cart").Translate("title", "Cart"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void ForCategory_NoOverride_ReturnsTheDefault()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        Assert.Equal("Cart", context.ForCategory("Shop.Cart").Translate("title", "Cart"));
    }

    [Fact]
    public void ForCategory_NullCategory_Throws()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        Assert.Throws<ArgumentNullException>(() => context.ForCategory(null!));
    }
}
