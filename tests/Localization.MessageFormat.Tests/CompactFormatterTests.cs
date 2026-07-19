using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CompactFormatterTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    // Below the smallest bucket: plain render with the compact 0/1 fraction (|value| < 10 -> 1 else 0).
    [InlineData("5.5", "5.5")]
    [InlineData("9.99", "10")]        // |9.99| < 10 -> 1 frac -> 10.0 -> trims to "10"
    [InlineData("12.5", "13")]        // |12.5| >= 10 -> 0 frac -> away-from-zero -> "13"
    [InlineData("123.45", "123")]
    [InlineData("999.4", "999")]
    [InlineData("999.9", "1K")]       // 0 frac -> 1000 carries into the real 1000 bucket -> "1K"
    // Compact suffix (unchanged path).
    [InlineData("1234", "1.2K")]
    [InlineData("12345", "12K")]
    [InlineData("123456", "123K")]
    [InlineData("999999", "1M")]      // round-up re-buckets 100000 -> 1000000
    [InlineData("1000000", "1M")]
    [InlineData("1500000", "1.5M")]
    [InlineData("1000000000", "1B")]
    [InlineData("1000000000000", "1T")]
    public void Format_ShortDecimalEnglish_MatchesIntl(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _en));
    }

    [Theory]
    [InlineData("-12.5", "-13")]
    [InlineData("-1234", "-1.2K")]
    [InlineData("-999.9", "-1K")]     // carry preserves the sign
    public void Format_ShortDecimalEnglish_Negative(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _en));
    }

    [Theory]
    // German short-decimal has "0" sentinel buckets below a million (no sub-million compaction): those render
    // plain through the standard pattern with the compact fraction and ECMA-402 min2 grouping, byte-matching
    // Intl. min2 keeps 4-digit values separatorless (1234) but groups 5+ (12345).
    [InlineData("12.5", "13")]
    [InlineData("1234", "1234")]                    // min2: 4 integer digits -> no separator (was "1.234" pre-parity)
    [InlineData("12345", "12.345")]                 // 5 digits -> grouped
    [InlineData("123456", "123.456")]
    [InlineData("999999", "999.999")]               // atop the 100000 sentinel; does NOT round into the million bucket
    [InlineData("999.9", "1000")]                   // 0 frac -> 1000 carries into the 1000 sentinel -> plain "1000"
    [InlineData("1000000", "1\u00A0Mio.")]          // real bucket -> compact; NBSP joiner, literal dot ("0 Mio'.'")
    [InlineData("1200000", "1,2\u00A0Mio.")]        // comma decimal, NBSP joiner
    [InlineData("1500000", "1,5\u00A0Mio.")]
    [InlineData("1234567", "1,2\u00A0Mio.")]
    public void Format_ShortDecimalGerman_MatchesIntl(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _de));
    }

    [Theory]
    [InlineData("-1234", "-1234")]              // sentinel/plain level: min2 stays ungrouped, minus prefixed
    [InlineData("-1200000", "-1,2\u00A0Mio.")]  // real bucket: NBSP joiner survives the derived minus
    public void Format_ShortDecimalGerman_Negative(string value, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D(value), "::compact-short", _de));
    }

    [Fact]
    public void Format_CurrencyCompactEnglish_Negative()
    {
        // Intl en compact-short currency USD of -1234 -> "-$1.2K": the sign combines with the currency
        // prefix and the compact suffix in the same call, not just a plain decimal negative.
        Assert.Equal("-$1.2K", NumberFormatting.Format(D("-1234"), "::currency/USD compact-short", _en));
    }

    [Fact]
    public void Format_CompactShort_GroupOffSuppressesMin2Grouping()
    {
        // The de fixture above shows "12345" -> "12.345" (min2 grouping) with grouping left on; group-off
        // must suppress that separator end-to-end through NumberFormatting.Format, not just at parse time.
        Assert.Equal("12345", NumberFormatting.Format(D("12345"), "::compact-short group-off", _de));
    }

    [Fact]
    public void Format_CompactShort_BaseLanguageLocaleFallback()
    {
        // "zh-CN" is absent from the pinned compact set but its base language "zh" is present: CompactFormatter
        // must resolve through the middle fallback tier, matching formatting via "zh" directly.
        Assert.Equal(
            NumberFormatting.Format(D("1234"), "::compact-short", CultureInfo.GetCultureInfo("zh")),
            NumberFormatting.Format(D("1234"), "::compact-short", CultureInfo.GetCultureInfo("zh-CN")));
    }

    [Fact]
    public void Format_CompactShort_InvariantCulture_FallsBackToRootBucketData()
    {
        // CultureInfo.InvariantCulture.Name is "" -- absent from the pinned compact set with no
        // base-language segment either, so this exercises the root tier directly. Root's short-decimal
        // 1000-magnitude bucket pattern is the plain "0K" (no locale-specific joiner or suffix word).
        Assert.Equal("1.2K", NumberFormatting.Format(D("1234"), "::compact-short", CultureInfo.InvariantCulture));
    }
}
