using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

// Exhaustive CLDR currency-display parity. Every expected value below is derived byte-for-byte from the
// ICU 78 / CLDR 48 oracle (eng/oracle/icu-format.cs) -- the same pinned data our engine reads -- via one
// batch run. The matrix sweeps all four unit widths (short symbol, narrow, iso-code, full-name) across
// en/de/fr/ja/pl and the currencies that exercise each distinct behaviour: prefix symbol (USD/en), suffix
// symbol (USD/fr "$US", EUR/de+fr), alphabetic disambiguation with currencySpacing NBSP (SEK/en), narrow-
// symbol fallback (CAD/en "CA$" -> "$"), 0-minor-unit rounding (JPY), 3-minor-unit (BHD/KWD), plus
// negatives and the plural categories one/few/many/other. Polish few/many are only reachable on a 0-minor
// currency: a 2-minor currency always renders two fraction digits (v=2), which forces the "other"
// category, so the pl plural rows use JPY (v=0) to hit one/few/many.
//
// The currencySpacing joiner (NBSP) and the French group separator (NNBSP) appear only as backslash-u
// escape sequences below, never as literal bytes.
//
// Two number-engine features are intentionally OUT OF SCOPE here (Phase B, not currency display, verified
// to affect plain decimals equally; the SPEC does not specify either):
//   * Arabic-Indic digit shaping (ar): our engine renders Latin digits, so the matrix is restricted to
//     Latin-digit locales -- the omission is explicit, not a silent skip.
//   * minimumGroupingDigits: pl declares minimumGroupingDigits=2, so ICU suppresses the group separator
//     when the lead group is a single digit (1234 -> "1234", but 12345 -> "12<NBSP>345"). Our engine has
//     no minimumGroupingDigits handling and always groups. The pl width-sweep therefore uses 12345.67
//     (5 integer digits, grouping ON in both engines) instead of 1234.56, so these rows exercise pl
//     currency display and NBSP grouping without asserting the unimplemented suppression.
public sealed class CurrencyDisplayParityTests
{
    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    [Theory]
    [InlineData("en-US", "currency/USD", "1234.56", "$1,234.56")]
    [InlineData("en-US", "currency/USD", "-1234.56", "-$1,234.56")]
    [InlineData("en-US", "currency/USD unit-width-narrow", "1234.56", "$1,234.56")]
    [InlineData("en-US", "currency/USD unit-width-narrow", "-1234.56", "-$1,234.56")]
    [InlineData("en-US", "currency/USD unit-width-iso-code", "1234.56", "USD\u00A01,234.56")]
    [InlineData("en-US", "currency/USD unit-width-iso-code", "-1234.56", "-USD\u00A01,234.56")]
    [InlineData("en-US", "currency/USD unit-width-full-name", "1234.56", "1,234.56 US dollars")]
    [InlineData("en-US", "currency/USD unit-width-full-name", "-1234.56", "-1,234.56 US dollars")]
    [InlineData("de-DE", "currency/USD", "1234.56", "1.234,56\u00A0$")]
    [InlineData("de-DE", "currency/USD", "-1234.56", "-1.234,56\u00A0$")]
    [InlineData("de-DE", "currency/USD unit-width-narrow", "1234.56", "1.234,56\u00A0$")]
    [InlineData("de-DE", "currency/USD unit-width-narrow", "-1234.56", "-1.234,56\u00A0$")]
    [InlineData("de-DE", "currency/USD unit-width-iso-code", "1234.56", "1.234,56\u00A0USD")]
    [InlineData("de-DE", "currency/USD unit-width-iso-code", "-1234.56", "-1.234,56\u00A0USD")]
    [InlineData("de-DE", "currency/USD unit-width-full-name", "1234.56", "1.234,56 US-Dollar")]
    [InlineData("de-DE", "currency/USD unit-width-full-name", "-1234.56", "-1.234,56 US-Dollar")]
    [InlineData("fr-FR", "currency/USD", "1234.56", "1\u202F234,56\u00A0$US")]
    [InlineData("fr-FR", "currency/USD", "-1234.56", "-1\u202F234,56\u00A0$US")]
    [InlineData("fr-FR", "currency/USD unit-width-narrow", "1234.56", "1\u202F234,56\u00A0$")]
    [InlineData("fr-FR", "currency/USD unit-width-narrow", "-1234.56", "-1\u202F234,56\u00A0$")]
    [InlineData("fr-FR", "currency/USD unit-width-iso-code", "1234.56", "1\u202F234,56\u00A0USD")]
    [InlineData("fr-FR", "currency/USD unit-width-iso-code", "-1234.56", "-1\u202F234,56\u00A0USD")]
    [InlineData("fr-FR", "currency/USD unit-width-full-name", "1234.56", "1\u202F234,56 dollars des États-Unis")]
    [InlineData("fr-FR", "currency/USD unit-width-full-name", "-1234.56", "-1\u202F234,56 dollars des États-Unis")]
    [InlineData("ja-JP", "currency/USD", "1234.56", "$1,234.56")]
    [InlineData("ja-JP", "currency/USD", "-1234.56", "-$1,234.56")]
    [InlineData("ja-JP", "currency/USD unit-width-narrow", "1234.56", "$1,234.56")]
    [InlineData("ja-JP", "currency/USD unit-width-narrow", "-1234.56", "-$1,234.56")]
    [InlineData("ja-JP", "currency/USD unit-width-iso-code", "1234.56", "USD\u00A01,234.56")]
    [InlineData("ja-JP", "currency/USD unit-width-iso-code", "-1234.56", "-USD\u00A01,234.56")]
    [InlineData("ja-JP", "currency/USD unit-width-full-name", "1234.56", "1,234.56米ドル")]
    [InlineData("ja-JP", "currency/USD unit-width-full-name", "-1234.56", "-1,234.56米ドル")]
    [InlineData("pl-PL", "currency/USD", "12345.67", "12\u00A0345,67\u00A0USD")]
    [InlineData("pl-PL", "currency/USD", "-12345.67", "-12\u00A0345,67\u00A0USD")]
    [InlineData("pl-PL", "currency/USD unit-width-narrow", "12345.67", "12\u00A0345,67\u00A0$")]
    [InlineData("pl-PL", "currency/USD unit-width-narrow", "-12345.67", "-12\u00A0345,67\u00A0$")]
    [InlineData("pl-PL", "currency/USD unit-width-iso-code", "12345.67", "12\u00A0345,67\u00A0USD")]
    [InlineData("pl-PL", "currency/USD unit-width-iso-code", "-12345.67", "-12\u00A0345,67\u00A0USD")]
    [InlineData("pl-PL", "currency/USD unit-width-full-name", "12345.67", "12\u00A0345,67 dolara amerykańskiego")]
    [InlineData("pl-PL", "currency/USD unit-width-full-name", "-12345.67", "-12\u00A0345,67 dolara amerykańskiego")]
    [InlineData("en-US", "currency/USD unit-width-full-name", "1", "1.00 US dollars")]
    [InlineData("en-US", "currency/USD unit-width-full-name", "2", "2.00 US dollars")]
    [InlineData("fr-FR", "currency/USD unit-width-full-name", "1", "1,00 dollar des États-Unis")]
    [InlineData("fr-FR", "currency/USD unit-width-full-name", "2", "2,00 dollars des États-Unis")]
    [InlineData("de-DE", "currency/USD unit-width-full-name", "1", "1,00 US-Dollar")]
    [InlineData("ja-JP", "currency/USD unit-width-full-name", "1", "1.00米ドル")]
    [InlineData("pl-PL", "currency/JPY unit-width-full-name", "1", "1 jen japoński")]
    [InlineData("pl-PL", "currency/JPY unit-width-full-name", "2", "2 jeny japońskie")]
    [InlineData("pl-PL", "currency/JPY unit-width-full-name", "5", "5 jenów japońskich")]
    [InlineData("pl-PL", "currency/JPY unit-width-full-name", "22", "22 jeny japońskie")]
    [InlineData("en-US", "currency/SEK", "1234.56", "SEK\u00A01,234.56")]
    [InlineData("en-US", "currency/SEK unit-width-narrow", "1234.56", "kr\u00A01,234.56")]
    [InlineData("en-US", "currency/SEK unit-width-iso-code", "1234.56", "SEK\u00A01,234.56")]
    [InlineData("en-US", "currency/SEK unit-width-full-name", "1234.56", "1,234.56 Swedish kronor")]
    [InlineData("en-US", "currency/CAD", "1234.56", "CA$1,234.56")]
    [InlineData("en-US", "currency/CAD unit-width-narrow", "1234.56", "$1,234.56")]
    [InlineData("en-US", "currency/JPY", "1234.56", "¥1,235")]
    [InlineData("en-US", "currency/JPY unit-width-iso-code", "1234.56", "JPY\u00A01,235")]
    [InlineData("en-US", "currency/JPY unit-width-full-name", "1", "1 Japanese yen")]
    [InlineData("en-US", "currency/JPY unit-width-full-name", "2", "2 Japanese yen")]
    [InlineData("ja-JP", "currency/JPY", "1234.56", "￥1,235")]
    [InlineData("en-US", "currency/BHD", "1234.56", "BHD\u00A01,234.560")]
    [InlineData("en-US", "currency/BHD unit-width-iso-code", "1234.56", "BHD\u00A01,234.560")]
    [InlineData("en-US", "currency/KWD", "2.5", "KWD\u00A02.500")]
    [InlineData("de-DE", "currency/EUR", "1234.56", "1.234,56\u00A0€")]
    [InlineData("fr-FR", "currency/EUR", "1234.56", "1\u202F234,56\u00A0€")]
    public void Format_MatchesIcuOracle(string locale, string skeleton, string value, string expected) =>
        Assert.Equal(
            expected,
            NumberFormatting.Format(decimal.Parse(value, CultureInfo.InvariantCulture), "::" + skeleton, Culture(locale)));
}
