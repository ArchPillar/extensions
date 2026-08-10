using System.Reflection;
using System.Globalization;
using ArchPillar.Extensions.Localization.Providers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace ArchPillar.Extensions.Localization.AspNetCore.Tests;

/// <summary>
/// The MVC seam for <c>[Localized]</c>. The DataAnnotations localizer alone cannot serve it: MVC only asks that
/// localizer to translate strings it already found on a system attribute, so a member carrying just
/// <c>[Localized]</c> would fall back to its property name. This provider fills the display metadata directly,
/// under the declaring type's category, so <c>[Localized]</c> and <c>[Display]</c> render identically.
/// </summary>
public sealed class LocalizedDisplayMetadataProviderTests
{
    private sealed class RegisterModel
    {
        [Localized("register.password.label", "Password", DescriptionKey = "register.password.help", Description = "At least 12 characters.")]
        public string Password { get; set; } = "";

        public string Unlabelled { get; set; } = "";
    }

    private static readonly string _category = typeof(RegisterModel).FullName!;

    [Fact]
    public void CreateDisplayMetadata_Localized_SetsTheDisplayNameFromItsDefault()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        DisplayMetadata metadata = Describe(nameof(RegisterModel.Password), context);

        Assert.Equal("Password", metadata.DisplayName!());
        Assert.Equal("At least 12 characters.", metadata.Description!());
    }

    [Fact]
    public void CreateDisplayMetadata_NoLocalized_LeavesTheMetadataAlone()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        DisplayMetadata metadata = Describe(nameof(RegisterModel.Unlabelled), context);

        // Untouched, so MVC keeps its own conventions (the property name) rather than us inventing a label.
        Assert.Null(metadata.DisplayName);
        Assert.Null(metadata.Description);
    }

    [Fact]
    public void CreateDisplayMetadata_Override_ResolvesUnderTheStableKeyPerCall()
    {
        using LocalizationContext context = ContextWith(("register.password.label", "Passwort"), ("register.password.help", "Mindestens 12 Zeichen."));

        DisplayMetadata metadata = Describe(nameof(RegisterModel.Password), context);

        // Metadata is cached by MVC, so resolution must happen when the delegate runs — under that call's culture.
        Assert.Equal("Password", metadata.DisplayName!());
        Assert.Equal("Passwort", InCulture("de", () => metadata.DisplayName!()!));
        Assert.Equal("Mindestens 12 Zeichen.", InCulture("de", () => metadata.Description!()!));
    }

    private static DisplayMetadata Describe(string propertyName, LocalizationContext context)
    {
        PropertyInfo property = typeof(RegisterModel).GetProperty(propertyName)!;
        var key = ModelMetadataIdentity.ForProperty(property, property.PropertyType, typeof(RegisterModel));
        var providerContext = new DisplayMetadataProviderContext(key, ModelAttributes.GetAttributesForProperty(typeof(RegisterModel), property));

        new LocalizedDisplayMetadataProvider(context.ForCategory).CreateDisplayMetadata(providerContext);
        return providerContext.DisplayMetadata;
    }

    private static LocalizationContext ContextWith(params (string Key, string Translated)[] entries) =>
        new(new LocalizerOptions
        {
            SourceCulture = "en",
            Providers =
            [
                _ => new InMemoryCatalogProvider(
                [
                    new Catalog
                    {
                        Culture = "de",
                        Entries = [.. entries.Select(entry => new CatalogEntry
                        {
                            Category = _category,
                            Key = entry.Key,
                            SourceMessage = entry.Key,
                            TranslatedMessage = entry.Translated,
                            SourceFingerprint = "",
                            State = TranslationState.Translated,
                        })],
                    },
                ]),
            ],
        });

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
