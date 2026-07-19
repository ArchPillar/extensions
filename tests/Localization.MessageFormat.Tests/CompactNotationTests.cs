using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CompactNotationTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");

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

    [Theory]
    [InlineData("12345", "12.345")]                 // sentinel (de: no sub-million compaction) -> deferred to standard
    [InlineData("123456", "123.456")]               // sentinel deferred
    [InlineData("999999", "999.999")]               // sentinel deferred; NOT re-bucketed to a million (matches Intl)
    [InlineData("1200000", "1,2\u00A0Mio.")]        // Intl de compact short; NBSP joiner (pattern "0 Mio'.'")
    [InlineData("1000000", "1\u00A0Mio.")]          // exact million -> singular affix, compacted 1.0 trims to "1"
    [InlineData("1500000", "1,5\u00A0Mio.")]        // NBSP joiner
    public void Format_ShortDecimalGerman(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _de));
    }

    [Fact]
    public void Format_ShortDecimalGerman_FourDigit_DivergesFromIntlMin2Grouping()
    {
        // KNOWN LIMITATION (Task 6): Intl compact notation uses min2 grouping, so a 4-digit deferred value
        // drops the group separator (Intl de "1234"). We defer sentinel/small values to the standard path,
        // which groups ("1.234"). Everything from 5 digits up matches Intl. This asserts OUR behavior.
        Assert.Equal("1.234", NumberFormatting.Format(D("1234"), "::compact-short", _de));
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
}
