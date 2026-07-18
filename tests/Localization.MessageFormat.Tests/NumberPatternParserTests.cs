using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class NumberPatternParserTests
{
    [Fact]
    public void Parse_PlainDecimal_HasNoAffixesAndCldrBounds()
    {
        NumberPattern pattern = NumberPatternParser.Parse("#,##0.###");

        Assert.Empty(pattern.PositivePrefix);
        Assert.Empty(pattern.PositiveSuffix);
        Assert.Null(pattern.NegativePrefix);
        Assert.Equal(1, pattern.MinIntegerDigits);
        Assert.Equal(0, pattern.MinFractionDigits);
        Assert.Equal(3, pattern.MaxFractionDigits);
        Assert.False(pattern.IsPercent);
    }

    [Fact]
    public void Parse_CurrencyPrefix_TokenizesSymbol()
    {
        NumberPattern pattern = NumberPatternParser.Parse("¤#,##0.00");

        PatternToken token = Assert.Single(pattern.PositivePrefix);
        Assert.Equal(PatternTokenKind.CurrencySymbol, token.Kind);
        Assert.Equal(2, pattern.MinFractionDigits);
        Assert.Equal(2, pattern.MaxFractionDigits);
    }

    [Fact]
    public void Parse_CurrencySuffixWithNbsp_PreservesExactLiteral()
    {
        NumberPattern pattern = NumberPatternParser.Parse("#,##0.00 ¤");

        Assert.Equal(2, pattern.PositiveSuffix.Count);
        Assert.Equal(PatternTokenKind.Literal, pattern.PositiveSuffix[0].Kind);
        Assert.Equal(" ", pattern.PositiveSuffix[0].Text);
        Assert.Equal(PatternTokenKind.CurrencySymbol, pattern.PositiveSuffix[1].Kind);
    }

    [Fact]
    public void Parse_Percent_SetsFlagAndToken()
    {
        NumberPattern pattern = NumberPatternParser.Parse("#,##0 %");

        Assert.True(pattern.IsPercent);
        Assert.Equal(PatternTokenKind.PercentSign, pattern.PositiveSuffix[1].Kind);
        Assert.Equal(0, pattern.MaxFractionDigits);
    }

    [Fact]
    public void Parse_NegativeSubpattern_CapturesItsAffixes()
    {
        NumberPattern pattern = NumberPatternParser.Parse("¤ #,##0.00;¤ -#,##0.00");

        Assert.NotNull(pattern.NegativePrefix);
        Assert.Equal(PatternTokenKind.CurrencySymbol, pattern.NegativePrefix![0].Kind);
        Assert.Equal(PatternTokenKind.Literal, pattern.NegativePrefix[1].Kind);
        Assert.Equal(PatternTokenKind.MinusSign, pattern.NegativePrefix[2].Kind);
        Assert.Empty(pattern.NegativeSuffix!);
    }

    [Fact]
    public void Parse_QuotedLiterals_UnquoteAndEscape()
    {
        // 'Cost:' is literal text; '' is a literal apostrophe; the quoted '%' must NOT become a percent token.
        NumberPattern pattern = NumberPatternParser.Parse("'Cost:'' ''%'0.0");

        Assert.Equal(PatternTokenKind.Literal, pattern.PositivePrefix[0].Kind);
        Assert.Equal("Cost:' '%", pattern.PositivePrefix[0].Text);
        Assert.False(pattern.IsPercent);
        Assert.Equal(1, pattern.MinFractionDigits);
    }

    [Fact]
    public void Parse_DoubleCurrencySign_IsIsoCodeToken()
    {
        NumberPattern pattern = NumberPatternParser.Parse("¤¤#,##0.00");

        Assert.Equal(PatternTokenKind.CurrencyCode, Assert.Single(pattern.PositivePrefix).Kind);
    }

    [Theory]
    [InlineData("#,##0.###‰")]   // per-mille unsupported
    [InlineData("0;0;0")]        // more than one ';'
    [InlineData("¤¤¤0")]         // display-name currency placeholder unsupported
    [InlineData("0.#0")]         // '#' before '0' in fraction
    public void Parse_UnsupportedSyntax_Throws(string pattern)
    {
        Assert.Throws<FormatException>(() => NumberPatternParser.Parse(pattern));
    }

    [Fact]
    public void Parse_SameInstance_IsCached()
    {
        Assert.Same(NumberPatternParser.Parse("#,##0.###"), NumberPatternParser.Parse("#,##0.###"));
    }
}
