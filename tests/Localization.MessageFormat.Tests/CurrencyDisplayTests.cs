using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CurrencyDisplayTests
{
    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    // Width is passed as int: the CurrencyWidth enum is internal, so a public [Theory] parameter of that
    // type would be less accessible than the method (CS0051). Matches NumberSkeletonTests' width fixtures.
    [Theory]
    [InlineData("en-US", "USD", (int)CurrencyWidth.Short, "$")]
    [InlineData("en-US", "USD", (int)CurrencyWidth.IsoCode, "USD")]
    [InlineData("en-US", "SEK", (int)CurrencyWidth.Short, "SEK")]
    [InlineData("en-US", "SEK", (int)CurrencyWidth.Narrow, "kr")]
    [InlineData("fr-FR", "USD", (int)CurrencyWidth.Short, "$US")]
    [InlineData("en-US", "CAD", (int)CurrencyWidth.Short, "CA$")]
    [InlineData("en-US", "CAD", (int)CurrencyWidth.Narrow, "$")]
    [InlineData("en-US", "USD", (int)CurrencyWidth.Narrow, "$")]   // narrow empty -> falls back to symbol
    public void Glyph_MatchesCldr(string culture, string code, int width, string expected) =>
        Assert.Equal(expected, CurrencyDisplay.Glyph(code, Culture(culture), (CurrencyWidth)width));

    [Fact]
    public void Glyph_RegionalLocale_InheritsLanguage() =>
        // fr-CA carries no EUR of its own (delta-dropped, identical to fr); the chain walk fr-ca -> fr
        // resolves the language's "€". Proves regional locales inherit currency display from their language.
        Assert.Equal("€", CurrencyDisplay.Glyph("EUR", Culture("fr-CA"), CurrencyWidth.Short));

    [Fact]
    public void Glyph_UnknownCode_FallsBackToCode() =>
        Assert.Equal("ZZZ", CurrencyDisplay.Glyph("ZZZ", Culture("en-US"), CurrencyWidth.Short));

    [Theory]
    [InlineData("en-US", "USD", "other", "US dollars")]
    [InlineData("en-US", "USD", "one", "US dollar")]
    [InlineData("de-DE", "USD", "other", "US-Dollar")]
    public void Name_MatchesCldr(string culture, string code, string plural, string expected) =>
        Assert.Equal(expected, CurrencyDisplay.Name(code, Culture(culture), plural));
}
