using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class PatternRendererTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");

    private static string Render(string pattern, decimal value, PatternPrecision precision, CultureInfo culture,
        bool grouping = true, string symbol = "$", string code = "USD") =>
        PatternRenderer.Render(NumberPatternParser.Parse(pattern), value, precision, grouping, culture, symbol, code);

    [Fact]
    public void Render_PlainDecimal_UsesCultureAtoms()
    {
        Assert.Equal("1,234.5", Render("#,##0.###", 1234.5m, PatternPrecision.Fraction(0, 3), _en));
        Assert.Equal("1.234,5", Render("#,##0.###", 1234.5m, PatternPrecision.Fraction(0, 3), _de));
    }

    [Fact]
    public void Render_CurrencyAffix_SubstitutesSymbolWithExactSpacing()
    {
        Assert.Equal("$1,234.50", Render("¤#,##0.00", 1234.5m, PatternPrecision.Fraction(2, 2), _en));
        Assert.Equal("1.234,50\u00A0$", Render("#,##0.00\u00A0¤", 1234.5m, PatternPrecision.Fraction(2, 2), _de));
    }

    [Fact]
    public void Render_IsoCodePlaceholder_SubstitutesCode()
    {
        Assert.Equal("USD 1,234.50", Render("¤¤ #,##0.00", 1234.5m, PatternPrecision.Fraction(2, 2), _en));
    }

    [Fact]
    public void Render_Percent_ScalesBy100AndUsesLocaleSign()
    {
        Assert.Equal("54%", Render("#,##0%", 0.535m, PatternPrecision.Fraction(0, 0), _en));
        Assert.Equal("53.5%", Render("#,##0%", 0.535m, PatternPrecision.Fraction(0, 1), _en));
    }

    [Fact]
    public void Render_NegativeWithoutSubpattern_DerivesMinusPrefix()
    {
        Assert.Equal("-$1,234.50", Render("¤#,##0.00", -1234.5m, PatternPrecision.Fraction(2, 2), _en));
    }

    [Fact]
    public void Render_NegativeSubpattern_UsesItsAffixes()
    {
        var result = Render("¤\u00A0#,##0.00;¤\u00A0-#,##0.00", -1234.5m, PatternPrecision.Fraction(2, 2), _en, symbol: "€");

        Assert.Equal("€\u00A0-1,234.50", result);
    }

    [Fact]
    public void Render_GroupOff_DropsGroupSeparators()
    {
        Assert.Equal("$1234.50", Render("¤#,##0.00", 1234.5m, PatternPrecision.Fraction(2, 2), _en, grouping: false));
    }

    [Theory]
    [InlineData("1234", 2, "1200")]
    [InlineData("1250", 2, "1300")]
    [InlineData("1.234", 2, "1.2")]
    [InlineData("0.001234", 2, "0.0012")]
    [InlineData("999999", 2, "1000000")]
    [InlineData("1000", 2, "1000")]
    [InlineData("100", 1, "100")]
    [InlineData("0.001", 2, "0.001")]
    [InlineData("-1234", 2, "-1200")]
    public void RoundToSignificant_RoundsHalfAway(string value, int digits, string expected)
    {
        var rounded = PatternRenderer.RoundToSignificant(
            decimal.Parse(value, CultureInfo.InvariantCulture), digits);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), rounded);
    }

    [Fact]
    public void Render_SignificantPrecision_TrimsTrailingZeros()
    {
        Assert.Equal("1.2", Render("#,##0.###", 1.234m, PatternPrecision.Significant(2), _en));
        Assert.Equal("1,200", Render("#,##0.###", 1234m, PatternPrecision.Significant(2), _en));
    }
}
