using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CompactNotationTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo _hi = CultureInfo.GetCultureInfo("hi-IN");

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("1234", "1.2K")]
    [InlineData("12345", "12K")]
    [InlineData("123456", "123K")]
    [InlineData("1500000", "1.5M")]
    [InlineData("1000000000", "1B")]
    public void Format_ShortDecimalEnglish(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _en));
    }

    [Theory]
    [InlineData("1234", "1.2 thousand")]
    [InlineData("3000000", "3 million")]
    public void Format_LongDecimalEnglish(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-long", _en));
    }

    [Fact]
    public void Format_BelowThreshold_FallsThroughToStandard()
    {
        // < 1000 is not compacted: identical to the default decimal render.
        Assert.Equal(
            NumberFormatting.Format(D("950"), null, _en),
            NumberFormatting.Format(D("950"), "::compact-short", _en));
    }

    [Fact]
    public void Format_LongDecimalGerman_ExactMillion_SelectsSingular()
    {
        // de long: count-one vs count-other on the COMPACTED value. Exact 1e6 -> "1 Million"; 1.2e6 -> "1,2 Millionen".
        // de longDecimal pattern joiner is a REGULAR space (U+0020), confirmed against compact.json.
        Assert.Equal("1 Million", NumberFormatting.Format(D("1000000"), "::compact-long", _de));
        Assert.Equal("1,2 Millionen", NumberFormatting.Format(D("1200000"), "::compact-long", _de));
    }

    [Fact]
    public void Format_LongDecimalFrench_ExplicitValue_RendersLiteralMille()
    {
        // CLDR explicit-value count: French ::compact-long of a value that compacts to exactly 1 (1000..1049)
        // renders the digit-less literal "mille", not "1 millier". Verified against Intl fr compactDisplay:long.
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal("mille", NumberFormatting.Format(D("1000"), "::compact-long", fr));
        Assert.Equal("mille", NumberFormatting.Format(D("1049"), "::compact-long", fr));
        // 1050 compacts to 1.1 -> count-one pattern "0 millier" (REGULAR space U+0020, per compact.json fr).
        Assert.Equal("1,1 millier", NumberFormatting.Format(D("1050"), "::compact-long", fr));
    }

    [Fact]
    public void Format_LongDecimalFrench_NegativeExplicitValue_RendersMinusMille()
    {
        // The explicit-value "mille" (fr long, compacted magnitude 1) must be reached for NEGATIVE values too:
        // -1000 -> "-mille", NOT "-1 millier" (which is what a signed comparison wrongly produced).
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal("-mille", NumberFormatting.Format(D("-1000"), "::compact-long", fr));
    }

    [Fact]
    public void Format_CurrencyCompactEnglish()
    {
        // Intl en compact short currency USD of 1234 -> "$1.2K".
        Assert.Equal("$1.2K", NumberFormatting.Format(D("1234"), "::currency/USD compact-short", _en));
    }

    [Fact]
    public void Format_CurrencyCompactLong_FallsBackToShort()
    {
        // C6: no long currency compact -- compact-long + currency uses the short-currency set.
        Assert.Equal(
            NumberFormatting.Format(D("1234"), "::currency/USD compact-short", _en),
            NumberFormatting.Format(D("1234"), "::currency/USD compact-long", _en));
    }

    [Fact]
    public void Format_AlphabeticCurrencySymbol_UsesAlphaSpacingVariant()
    {
        // An alphabetic symbol (ISO-code fallback) triggers the alphaNextToNumber variant, which inserts a
        // no-break space the base pattern lacks. ZZZ is unmatched -> symbol "ZZZ" (alphabetic), deterministic.
        var text = NumberFormatting.Format(D("1234"), "::currency/ZZZ compact-short", _en);
        Assert.Contains("ZZZ", text, StringComparison.Ordinal);
        Assert.Contains("\u00A0", text, StringComparison.Ordinal);   // NBSP boundary from the alpha variant
    }

    [Theory]
    // Currency (short): a plain value below the smallest bucket keeps the currency affix but overrides
    // minor units to the compact 0/1 fraction; the real bucket compacts with the suffix. Matches Intl.
    [InlineData("5.5", "$5.5")]
    [InlineData("9.99", "$10")]
    [InlineData("12.5", "$13")]
    [InlineData("123.45", "$123")]
    [InlineData("999.9", "$1K")]
    [InlineData("1234", "$1.2K")]
    public void Format_CurrencyShortEnglish_MatchesIntl(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::currency/USD compact-short", _en));
    }

    [Theory]
    // German currency (short): the plain render inherits the standard de currency NBSP joiner and min2
    // grouping while dropping the minor-unit .00 (compact 0 fraction); the million bucket compacts.
    [InlineData("1234", "1234\u00A0€")]
    [InlineData("12345", "12.345\u00A0€")]
    [InlineData("1000000", "1\u00A0Mio.\u00A0€")]
    public void Format_CurrencyShortGerman_MatchesIntl(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::currency/EUR compact-short", _de));
    }

    [Theory]
    // Non-Latin compact affix (Devanagari) with Latin digits and an NBSP joiner, over Indian magnitude
    // buckets. Derived from Intl.NumberFormat("hi",{notation:"compact"}); Devanagari letters stay literal.
    [InlineData("1234", "1.2\u00A0हज़ार")]
    [InlineData("12345", "12\u00A0हज़ार")]
    [InlineData("123456", "1.2\u00A0लाख")]
    [InlineData("1000000", "10\u00A0लाख")]
    public void Format_ShortDecimalHindi_MatchesIntl(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _hi));
    }
}
