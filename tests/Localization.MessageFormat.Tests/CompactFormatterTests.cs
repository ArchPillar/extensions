using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CompactFormatterTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");

    private static string? Short(decimal value) =>
        CompactFormatter.TryFormat(value, NumberNotation.CompactShort, NumberUnit.Decimal, _en, string.Empty, string.Empty);

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("1234", "1.2K")]
    [InlineData("12345", "12K")]
    [InlineData("123456", "123K")]     // fraction-digit rounding, NOT 2-significant "120K"
    [InlineData("1500000", "1.5M")]
    [InlineData("1000000", "1M")]      // exact power of ten trims to no fraction digits
    [InlineData("999999", "1M")]       // round-up re-buckets 100000 -> 1000000
    [InlineData("1000000000", "1B")]
    [InlineData("1000000000000", "1T")]
    public void TryFormat_ShortDecimalEnglish_MatchesIntl(string value, string expected)
    {
        Assert.Equal(expected, Short(D(value)));
    }

    [Fact]
    public void TryFormat_BelowThousand_ReturnsNull()
    {
        Assert.Null(Short(950m));
        Assert.Null(Short(0m));
        Assert.Null(Short(-12m));
    }

    [Fact]
    public void TryFormat_Negative_PrefixesMinus()
    {
        Assert.Equal("-1.2K", Short(D("-1234")));
    }

    [Fact]
    public void TryFormat_SentinelBucket_DefersToStandard()
    {
        // German short-decimal carries "0" sentinels below 1e6 (no sub-million compaction), so these defer
        // to standard (return null). 999999 sits atop the 100000 sentinel bucket and must NOT round up into
        // the real million bucket — it defers, matching Intl "999.999".
        var de = CultureInfo.GetCultureInfo("de-DE");
        Assert.Null(CompactFormatter.TryFormat(12345m, NumberNotation.CompactShort, NumberUnit.Decimal, de, string.Empty, string.Empty));
        Assert.Null(CompactFormatter.TryFormat(999999m, NumberNotation.CompactShort, NumberUnit.Decimal, de, string.Empty, string.Empty));
        // ...but the million bucket is real, so it compacts (non-null).
        Assert.NotNull(CompactFormatter.TryFormat(1000000m, NumberNotation.CompactShort, NumberUnit.Decimal, de, string.Empty, string.Empty));
    }
}
