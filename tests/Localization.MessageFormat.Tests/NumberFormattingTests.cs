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
        Assert.Equal("19,99\u00A0$", NumberFormatting.Format(D("19.99"), "::currency/USD", _de));
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

    [Fact]
    public void Format_CurrencyGroupOff_DropsGroupingSeparator()
    {
        Assert.Equal("$1234.50", NumberFormatting.Format(D("1234.5"), "::currency/USD group-off", _en));
    }

    [Fact]
    public void Format_CurrencyFractionOverride_WinsOverLookedUpMinorUnits()
    {
        // JPY's CLDR minor units are 0, so without an override the amount rounds to a whole number...
        var withoutOverride = NumberFormatting.Format(D("19.9"), "::currency/JPY", _en);
        Assert.Contains("20", withoutOverride, StringComparison.Ordinal);
        Assert.DoesNotContain("19.9", withoutOverride, StringComparison.Ordinal);

        // ...and an explicit .00 override must win over the looked-up 0 digits (spec D5).
        var withOverride = NumberFormatting.Format(D("19.9"), "::currency/JPY .00", _en);
        Assert.Contains("19.90", withOverride, StringComparison.Ordinal);
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
        // CLDR's percent pattern has zero fraction digits -- true Intl behavior rounds.
        Assert.Equal("54%", NumberFormatting.Format(0.535, "percent", _en));
        // An explicit fraction stem restores digits.
        Assert.Equal("53.5%", NumberFormatting.Format(0.535, "::percent .0#", _en));
    }

    [Fact]
    public void Format_Percent_UsesLocaleSpacingFromPattern()
    {
        Assert.Equal("50\u00A0%", NumberFormatting.Format(0.5, "percent", _de));
    }

    [Fact]
    public void Format_NegativeCurrency_UsesCldrShape()
    {
        // en's standard currency pattern has no negative subpattern -> derived minus prefix.
        Assert.Equal("-$19.99", NumberFormatting.Format(D("-19.99"), "::currency/USD", _en));
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

    [Fact]
    public void Format_CurrencyRangedFraction_TrimsTrailingZeros()
    {
        Assert.Equal("$1,234.5", NumberFormatting.Format(D("1234.5"), "::currency/USD .##", _en));
        Assert.Equal("$1,234", NumberFormatting.Format(D("1234"), "::currency/USD .##", _en));
        Assert.Equal("$1,234.5", NumberFormatting.Format(D("1234.5"), "::currency/USD .0#", _en));
    }

    [Fact]
    public void Format_BoolArgument_CoercesToOneOrZero()
    {
        // A bool routed to a number placeholder is coerced to 1/0 via TryToDecimal — unified with the
        // plural selector path (which already treats true as 1), not the pre-engine "True"/"False".
        Assert.Equal("1", NumberFormatting.Format(true, null, _en));
        Assert.Equal("0", NumberFormatting.Format(false, null, _en));
    }

    [Fact]
    public void TryToDecimal_DoubleOutOfDecimalRange_ReturnsFalse()
    {
        // A finite double beyond decimal's range must report false (not throw), matching the NaN/±infinity guards.
        Assert.False(NumberFormatting.TryToDecimal(double.MaxValue, out var number));
        Assert.Equal(0m, number);
    }

    [Fact]
    public void TryToDecimal_FloatOutOfDecimalRange_ReturnsFalse()
    {
        Assert.False(NumberFormatting.TryToDecimal(float.MaxValue, out var number));
        Assert.Equal(0m, number);
    }

    [Fact]
    public void Format_DoubleOutOfDecimalRange_FallsBackToOwnRendering()
    {
        // The formatter must not crash on a large double; it falls back to the double's own culture rendering.
        Assert.Equal(double.MaxValue.ToString(_en), NumberFormatting.Format(double.MaxValue, null, _en));
    }

    [Fact]
    public void Format_FloatOutOfDecimalRange_FallsBackToOwnRendering()
    {
        Assert.Equal(float.MaxValue.ToString(_en), NumberFormatting.Format(float.MaxValue, null, _en));
    }

    [Fact]
    public void Format_NormalRangeDouble_FormatsCorrectly()
    {
        // Control: an in-range double still formats through the decimal path.
        Assert.Equal("12.5", NumberFormatting.Format(12.5, null, _en));
    }
}
