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
}
