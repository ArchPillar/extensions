using System.Globalization;
using System.Numerics;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class NumberLocalizationExtensionsTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo _fr = CultureInfo.GetCultureInfo("fr-FR");

    // The same value+style rendered through the public message pipeline — the thing the extension must match.
    private static string ViaMessage(object value, string? style, CultureInfo culture)
    {
        var body = style is null ? "{v, number}" : $"{{v, number, {style}}}";
        return new MessageFormatter().Format(body, culture, ("v", value));
    }

    [Theory]
    [InlineData("::currency/USD")]
    [InlineData("::currency/USD unit-width-narrow")]     // both doors agree across all four currency widths
    [InlineData("::currency/USD unit-width-iso-code")]
    [InlineData("::currency/USD unit-width-full-name")]
    [InlineData("::compact-short")]
    [InlineData("::percent")]
    [InlineData("integer")]
    [InlineData(null)]
    public void ToLocalizedString_MatchesInMessageRendering_AcrossCultures(string? style)
    {
        const decimal Value = 1234.56m;   // const, PascalCase: RCS1118 (const local) is warnings-as-error in Release; constants must be PascalCase per .editorconfig
        foreach (CultureInfo culture in new[] { _en, _de, _fr })
        {
            Assert.Equal(ViaMessage(Value, style, culture), Value.ToLocalizedString(style, culture));
        }
    }

    [Fact]
    public void ToLocalizedString_NoCulture_FollowsCurrentUICulture_NotCurrentCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Formatting culture (en-US) and UI/translation culture (de-DE) deliberately differ.
            CultureInfo.CurrentCulture = _en;
            CultureInfo.CurrentUICulture = _de;

            // No explicit culture -> must follow CurrentUICulture (de), NOT CurrentCulture (en).
            // de USD renders "1.234,56<NBSP>$"; en would give "$1,234.56".
            Assert.Equal("1.234,56\u00A0$", 1234.56m.ToLocalizedString("::currency/USD"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ToLocalizedString_NullStyle_IsDefaultNumberFormat()
    {
        Assert.Equal("1,234.5", 1234.5m.ToLocalizedString(null, _en));
        Assert.Equal("1,234.5", 1234.5m.ToLocalizedString(culture: _en));
    }

    [Fact]
    public void ToLocalizedString_EachNumericType_Formats()
    {
        Assert.Equal("$1,234.50", 1234.50.ToLocalizedString("::currency/USD", _en));   // double
        Assert.Equal("$1,234.00", 1234.ToLocalizedString("::currency/USD", _en));       // int
        Assert.Equal("1.2K", 1234L.ToLocalizedString("::compact-short", _en));          // long
        Assert.Equal("$1,234.56", 1234.56m.ToLocalizedString("::currency/USD", _en));   // decimal
    }

    [Fact]
    public void ToLocalizedString_IFormattableOverload_ConvertibleValue_IcuFormats_ElseDegradesToToString()
    {
        // A BigInteger binds to the IFormattable catch-all (no implicit widening to the concrete four) and is
        // NOT IConvertible, so the engine cannot convert it -> it degrades to the value's own ToString (the
        // documented graceful fallback), NOT a compact form. This locks that contract honestly.
        BigInteger big = 1234567;
        Assert.Equal(big.ToString(null, _en), ((IFormattable)big).ToLocalizedString("::compact-short", _en));
    }

    [Fact]
    public void ToLocalizedString_InvalidSkeleton_ThrowsMessageFormatExceptionAtPositionMinusOne()
    {
        MessageFormatException ex = Assert.Throws<MessageFormatException>(
            () => 1m.ToLocalizedString("::currency/US", _en));
        Assert.Equal(-1, ex.Position);
    }
}
