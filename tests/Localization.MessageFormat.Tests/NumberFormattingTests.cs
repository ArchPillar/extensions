using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class NumberFormattingTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public void Format_CurrencyCode_UsesSpecifiedCurrencyRegardlessOfCulture()
    {
        Assert.Equal("$19.99", NumberFormatting.Format(D("19.99"), "::currency/USD", _en));
        Assert.Equal("19,99 $", NumberFormatting.Format(D("19.99"), "::currency/USD", _de));
    }

    [Fact]
    public void Format_CurrencyCode_UnknownCode_UsesIsoCodeAsSymbol()
    {
        Assert.Equal("ZZZ19.99", NumberFormatting.Format(D("19.99"), "::currency/ZZZ", _en));
    }

    [Fact]
    public void Format_NamedCurrency_UsesRenderingCultureCurrency()
    {
        Assert.Equal("$19.99", NumberFormatting.Format(D("19.99"), "currency", _en));
    }

    [Theory]
    [InlineData("::.00", "1.50")]
    [InlineData("::.##", "1.5")]
    [InlineData("::.0#", "1.5")]
    public void Format_Fractions_RespectMinMax(string style, string expected)
    {
        Assert.Equal(expected, NumberFormatting.Format(D("1.5"), style, _en));
    }

    [Fact]
    public void Format_Percent_IsIcuAligned()
    {
        Assert.Equal("50%", NumberFormatting.Format(0.5, "percent", _en));
        Assert.Equal("53.5%", NumberFormatting.Format(0.535, "percent", _en));
    }

    [Fact]
    public void Format_Integer_GroupsWithoutFractions()
    {
        Assert.Equal("1,234", NumberFormatting.Format(1234, "integer", _en));
    }

    [Fact]
    public void Format_GroupOff_DropsGroupingSeparator()
    {
        Assert.Equal("1234.5", NumberFormatting.Format(D("1234.5"), "::group-off", _en));
    }

    [Fact]
    public void Format_DefaultStyle_TrimsTrailingZeros()
    {
        Assert.Equal("1", NumberFormatting.Format(D("1.0"), null, _en));
        Assert.Equal("1.5", NumberFormatting.Format(D("1.50"), null, _en));
    }

    [Fact]
    public void Resolve_UnknownStyle_Throws()
    {
        Assert.Throws<MessageFormatException>(() => NumberFormatting.Resolve("currnecy"));
    }

    [Theory]
    [InlineData("1.0", 0)]
    [InlineData("1.50", 1)]
    [InlineData("1.567", 3)]
    [InlineData("2", 0)]
    public void VisibleFractionDigits_MatchesDefaultDisplay(string value, int expected)
    {
        Assert.Equal(expected, NumberFormatting.VisibleFractionDigits(D(value)));
    }
}
