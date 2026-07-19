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
        // de-DE's own currency is EUR: exercises the null-code ResolveCurrency branch for a second
        // locale, with a non-'$' symbol and NBSP joiner together.
        Assert.Equal("19,99\u00A0€", NumberFormatting.Format(D("19.99"), "currency", _de));
    }

    [Fact]
    public void Format_CurrencyGroupOff_DropsGroupingSeparator()
    {
        Assert.Equal("$1234.50", NumberFormatting.Format(D("1234.5"), "::currency/USD group-off", _en));
    }

    [Fact]
    public void Format_CurrencyFractionOverride_WinsOverLookedUpMinorUnits()
    {
        // JPY's CLDR minor units are 0, so without an override the amount rounds to a whole number
        // (CurrencyLookup resolves JPY's symbol to the fullwidth yen sign U+FFE5, per .NET's RegionInfo).
        Assert.Equal("￥20", NumberFormatting.Format(D("19.9"), "::currency/JPY", _en));

        // ...and an explicit .00 override must win over the looked-up 0 digits (spec D5).
        Assert.Equal("￥19.90", NumberFormatting.Format(D("19.9"), "::currency/JPY .00", _en));
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
    public void Format_NegativePercent_AppliesSignScaleAndRounding()
    {
        Assert.Equal("-50%", NumberFormatting.Format(-0.5, "percent", _en));
    }

    [Fact]
    public void Format_PercentWithGroupingVisible_UsesGroupSeparator()
    {
        // 12.34 scales to 1234%, the first percent fixture anywhere to exceed 999% and actually
        // exercise the default Grouping=true separator for NumberFormatSpec.Percent.
        Assert.Equal("1,234%", NumberFormatting.Format(D("12.34"), "percent", _en));
    }

    [Fact]
    public void Format_PercentGroupOff_DropsGroupingSeparator()
    {
        // 12.345 scales to 1234.5%, rounding away from zero to 1235% -- group-off must drop the separator
        // that would otherwise appear (see Format_PercentWithGroupingVisible_UsesGroupSeparator).
        Assert.Equal("1235%", NumberFormatting.Format(D("12.345"), "::percent group-off", _en));
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
    public void Format_Integer_RoundsFractionalInputAwayFromZero()
    {
        // The `integer` style's Min=0,Max=0 fraction bounds must actually round a fractional input,
        // not merely display zero fraction digits for an already-integral value.
        Assert.Equal("1,235", NumberFormatting.Format(D("1234.7"), "integer", _en));
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
    public void Format_NegativeDefaultStyle_DerivesMinusPrefix()
    {
        // The bare default style, end-to-end through Format (not PatternRenderer/compact/currency),
        // with a negative value: en's plain decimal pattern has no negative subpattern -> derived minus.
        Assert.Equal("-1,234.5", NumberFormatting.Format(D("-1234.5"), null, _en));
    }

    [Fact]
    public void Format_NaN_FallsBackToOwnRendering()
    {
        // FormatSpec's distinctive "don't throw, degrade gracefully" behavior for non-finite doubles,
        // exercised directly at the formatting-rule layer (not the plural path, which throws instead).
        Assert.Equal(double.NaN.ToString(_en), NumberFormatting.Format(double.NaN, null, _en));
    }

    [Fact]
    public void Format_PositiveInfinity_FallsBackToOwnRendering()
    {
        Assert.Equal(double.PositiveInfinity.ToString(_en), NumberFormatting.Format(double.PositiveInfinity, null, _en));
    }

    [Fact]
    public void Format_NegativeInfinity_FallsBackToOwnRendering()
    {
        Assert.Equal(double.NegativeInfinity.ToString(_en), NumberFormatting.Format(double.NegativeInfinity, null, _en));
    }

    [Fact]
    public void Format_NonNumericString_ReturnsVerbatim()
    {
        // A non-numeric string fails TryToDecimal and is not IFormattable-special-cased beyond its own
        // ToString(); string.ToString() returns itself verbatim.
        Assert.Equal("abc", NumberFormatting.Format("abc", null, _en));
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
    [InlineData("1.5678", 3)]   // rounds to "1.568" -- more than 3 fraction digits forces the round, not just a trim
    [InlineData("1.9995", 0)]   // rounds to "2.000" -> trims to 0 digits, changing both count and value
    [InlineData("-1.50", 1)]    // negative value: same trim/round math, sign must not perturb the digit count
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
