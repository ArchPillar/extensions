namespace ArchPillar.Extensions.Localization.Abstractions.Tests;

public sealed class TranslationKeyTests
{
    [Fact]
    public void ComposeQualified_PrependsCategoryAndSeparator()
    {
        Assert.Equal("Acme.Labels\u0004save", TranslationKey.ComposeQualified("Acme.Labels", "save"));
    }

    [Fact]
    public void ComposeQualified_GlobalCategory_IsSeparatorThenKey()
    {
        Assert.Equal("\u0004save", TranslationKey.ComposeQualified(string.Empty, "save"));
    }

    [Fact]
    public void ComposeQualified_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TranslationKey.ComposeQualified("Acme.Labels", null!));
    }
}
