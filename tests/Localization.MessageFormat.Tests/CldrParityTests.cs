using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

/// <summary>
/// Byte-level parity fixtures for the CLDR composition engine, derived from the pinned CLDR 48 patterns
/// and cross-checked against Intl.NumberFormat behavior for shape. Spacing characters are asserted exactly.
/// </summary>
public sealed class CldrParityTests
{
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static string Format(string locale, string? style, string value) =>
        NumberFormatting.Format(D(value), style, CultureInfo.GetCultureInfo(locale));

    [Theory]
    // en-US -- symbol prefix, minus derived, no spaces (fixed expectations).
    [InlineData("en-US", "::currency/USD", "1234.56", "$1,234.56")]
    [InlineData("en-US", "::currency/USD", "-1234.56", "-$1,234.56")]
    [InlineData("en-US", "percent", "0.5", "50%")]
    [InlineData("en-US", null, "1234.567", "1,234.567")]
    // hi-IN -- Indian grouping via NFI atoms (fixed expectation).
    [InlineData("hi-IN", null, "123456.789", "1,23,456.789")]
    public void Format_MatchesCldrComposition(string locale, string? style, string value, string expected)
    {
        Assert.Equal(expected, Format(locale, style, value));
    }

    [Fact]
    public void Format_German_UsesDataCarriedSpacing()
    {
        // Derived: de currency pattern is "#,##0.00<NBSP>¤" -- Intl shape "1.234,56 $"; joiner is U+00A0.
        Assert.Equal("1.234,56\u00A0$", Format("de-DE", "::currency/USD", "1234.56"));
        // Derived: de percent pattern is "#,##0<NBSP>%" -- Intl shape "50 %".
        Assert.Equal("50\u00A0%", Format("de-DE", "percent", "0.5"));
    }

    [Fact]
    public void Format_French_UsesDataCarriedSpacing()
    {
        // Derived: fr percent pattern is "#,##0<NBSP>%" (same pinned set as de) -- Intl shape "50 %".
        Assert.Equal("50\u00A0%", Format("fr-FR", "percent", "0.5"));
        // Derived: fr currency has TWO different invisible spaces -- the digit-internal group separator is
        // .NET's NFI atom for fr-FR, narrow no-break space U+202F, while the amount<->symbol joiner comes
        // from the pinned pattern's literal token, U+00A0. CLDR's fr symbol for USD is "$US" -- Intl shape
        // "1 234,56 $US".
        Assert.Equal("1\u202F234,56\u00A0$US", Format("fr-FR", "::currency/USD", "1234.56"));
    }

    [Fact]
    public void Format_Dutch_UsesNegativeSubpatternWhenDataCarriesOne()
    {
        // Derived: the pinned nl currency pattern is "¤<NBSP>#,##0.00;¤<NBSP>-#,##0.00" -- a genuine negative
        // subpattern, so the minus sits between the symbol and digits rather than being derived as a prefix.
        // CLDR's nl symbol for USD is "US$" -- Intl shape "US$ -1.234,56".
        Assert.Equal("US$\u00A0-1.234,56", Format("nl-NL", "::currency/USD", "-1234.56"));
    }

    [Fact]
    public void Format_Japanese_ZeroMinorUnits()
    {
        // ja's currency pattern is "¤#,##0.00" (symbol prefix, no space); JPY's minor units are 0, so
        // 1234.5 rounds away from zero to 1235 with no fraction. CLDR's ja symbol for JPY is the fullwidth
        // yen sign U+FFE5 (distinct from en's U+00A5), matching Intl's "￥1,235".
        Assert.Equal("￥1,235", Format("ja-JP", "::currency/JPY", "1234.5"));
    }

    [Fact]
    public void Format_German_NegativeCurrency_DerivesMinusWithNbspJoiner()
    {
        // de's currency pattern "#,##0.00<NBSP>¤" has no negative subpattern (no ';') -> derived minus,
        // prepended before the digits, with the pattern's own NBSP joiner still carried into the suffix.
        Assert.Equal("-1.234,56\u00A0$", Format("de-DE", "::currency/USD", "-1234.56"));
    }

    [Fact]
    public void Format_French_NegativeCurrency_DerivesMinusWithNarrowNbspGroupingAndNbspJoiner()
    {
        // Same pinned pattern as German (no negative subpattern) -> derived minus. The digit-internal
        // group separator is still fr-FR's NFI atom, narrow no-break space U+202F, distinct from the
        // pattern's own amount<->symbol joiner, U+00A0.
        Assert.Equal("-1\u202F234,56\u00A0$US", Format("fr-FR", "::currency/USD", "-1234.56"));
    }
}
