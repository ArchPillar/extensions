using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The reflection counterpart of the annotation extraction for types and members, for the consumers the ASP.NET
/// DataAnnotations pipeline does not reach. It resolves the same (category, key, default) the extractor wrote:
/// a self-contained <c>[Localized]</c> first, then a system attribute whose literal is the key with a
/// <c>[Localized…]</c> twin supplying its default.
/// </summary>
public sealed class MemberLocalizationExtensionsTests
{
    [Localized("models.register", "Registration")]
    private sealed class RegisterModel
    {
        [Localized("register.password.label", "Password")]
        public string Password { get; set; } = "";

        [Localized("user.email", "Email address", DescriptionKey = "user.email.help", Description = "We never share it.")]
        public string Email { get; set; } = "";

        [Localized("register.age", "Age", Description = "Years since birth.")]
        public int Age { get; set; }

        [Display(Name = "register.nickname.label")]
        [LocalizedDisplayName("Nickname")]
        public string Nickname { get; set; } = "";

        [DisplayName("Street address")]
        [Description("Where we post the letter.")]
        public string Street { get; set; } = "";

        public string Unlabelled { get; set; } = "";
    }

    [Fact]
    public void GetLocalizedDisplayName_Localized_ReturnsItsDefaultNotItsKey()
    {
        using var context = SourceOnly();
        Assert.Equal("Password", Property(nameof(RegisterModel.Password)).GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_SystemAttributeWithTwin_ReturnsTheTwinDefault()
    {
        using var context = SourceOnly();
        Assert.Equal("Nickname", Property(nameof(RegisterModel.Nickname)).GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_SystemAttributeAlone_IsTextAsKey()
    {
        using var context = SourceOnly();
        Assert.Equal("Street address", Property(nameof(RegisterModel.Street)).GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_NoAnnotation_ReturnsTheMemberName()
    {
        using var context = SourceOnly();
        Assert.Equal("Unlabelled", Property(nameof(RegisterModel.Unlabelled)).GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_OnTheTypeItself_ResolvesUnderItsOwnCategory()
    {
        // A Type is a MemberInfo, so a type's own display name needs no separate overload.
        using var context = SourceOnly();
        Assert.Equal("Registration", typeof(RegisterModel).GetLocalizedDisplayName(context));
    }

    [Fact]
    public void GetLocalizedDescription_LocalizedWithDescriptionKey_ReturnsItsText()
    {
        using var context = SourceOnly();
        Assert.Equal("We never share it.", Property(nameof(RegisterModel.Email)).GetLocalizedDescription(context));
    }

    [Fact]
    public void GetLocalizedDescription_SystemAttribute_IsTextAsKey()
    {
        using var context = SourceOnly();
        Assert.Equal("Where we post the letter.", Property(nameof(RegisterModel.Street)).GetLocalizedDescription(context));
    }

    [Fact]
    public void GetLocalizedDescription_NoDescription_ReturnsTheMemberName()
    {
        using var context = SourceOnly();
        Assert.Equal("Password", Property(nameof(RegisterModel.Password)).GetLocalizedDescription(context));
    }

    [Fact]
    public void GetLocalizedDisplayName_Expression_SelectsTheSameMember()
    {
        using var context = SourceOnly();
        Assert.Equal("Password", MemberLocalizationExtensions.GetLocalizedDisplayName<RegisterModel>(model => model.Password, context));
    }

    [Fact]
    public void GetLocalizedDisplayName_ExpressionOnAValueTypedMember_UnwrapsTheConversion()
    {
        // int reaches the object? return through a Convert node; the selector must still find the property.
        using var context = SourceOnly();
        Assert.Equal("Age", MemberLocalizationExtensions.GetLocalizedDisplayName<RegisterModel>(model => model.Age, context));
    }

    [Fact]
    public void GetLocalizedDescription_Expression_SelectsTheSameMember()
    {
        using var context = SourceOnly();
        Assert.Equal("Years since birth.", MemberLocalizationExtensions.GetLocalizedDescription<RegisterModel>(model => model.Age, context));
    }

    [Fact]
    public void GetLocalizedDisplayName_ExpressionThatIsNotAMember_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => MemberLocalizationExtensions.GetLocalizedDisplayName<RegisterModel>(model => model.Password.Length + 1));

        Assert.Contains("property or field", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLocalizedDisplayName_Override_ResolvesUnderTheStableKeyAndDeclaringTypeCategory()
    {
        using var context = WithOverride("register.password.label", "Passwort");

        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Passwort", Property(nameof(RegisterModel.Password)).GetLocalizedDisplayName(context));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void GetLocalizedDescription_Override_ResolvesUnderItsOwnDescriptionKey()
    {
        using var context = WithOverride("user.email.help", "Wir geben sie nicht weiter.");

        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Wir geben sie nicht weiter.", Property(nameof(RegisterModel.Email)).GetLocalizedDescription(context));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static PropertyInfo Property(string name) => typeof(RegisterModel).GetProperty(name)!;

    private static LocalizationContext SourceOnly() => new(new LocalizerOptions { SourceCulture = "en" });

    private static LocalizationContext WithOverride(string key, string translated) =>
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
                        Entries =
                        [
                            new CatalogEntry
                            {
                                Category = typeof(RegisterModel).FullName!,
                                Key = key,
                                SourceMessage = "",
                                TranslatedMessage = translated,
                                SourceFingerprint = "",
                                State = TranslationState.Translated,
                            },
                        ],
                    },
                ]),
            ],
        });
}
