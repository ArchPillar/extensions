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
        // Derived: de currency pattern is "#,##0.00<NBSP>\u00A4" -- Intl shape "1.234,56 $"; joiner is U+00A0.
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
        // from the pinned pattern's literal token, U+00A0 -- Intl shape "1 234,56 $".
        Assert.Equal("1\u202F234,56\u00A0$", Format("fr-FR", "::currency/USD", "1234.56"));
    }

    [Fact]
    public void Format_Dutch_UsesNegativeSubpatternWhenDataCarriesOne()
    {
        // Derived: the pinned nl currency pattern is "¤<NBSP>#,##0.00;¤<NBSP>-#,##0.00" -- a genuine negative
        // subpattern, so the minus sits between the symbol and digits rather than being derived as a prefix.
        // Intl shape "€ -1.234,56" (with our USD symbol substituted).
        Assert.Equal("$\u00A0-1.234,56", Format("nl-NL", "::currency/USD", "-1234.56"));
    }

    [Fact]
    public void Format_Japanese_ZeroMinorUnits()
    {
        var result = Format("ja-JP", "::currency/JPY", "1234.5");

        Assert.Contains("1,235", result, StringComparison.Ordinal);
        Assert.DoesNotContain(".", result, StringComparison.Ordinal);
    }
}
