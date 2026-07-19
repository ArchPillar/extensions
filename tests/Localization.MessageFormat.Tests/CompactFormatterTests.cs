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
}
