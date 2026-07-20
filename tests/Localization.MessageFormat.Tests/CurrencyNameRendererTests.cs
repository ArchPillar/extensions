using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CurrencyNameRendererTests
{
    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    [Theory]
    [InlineData("en-US", "USD", "1", "1.00 US dollars")]      // v=2 -> other, NOT one
    [InlineData("en-US", "USD", "1234.56", "1,234.56 US dollars")]
    [InlineData("en-US", "JPY", "1", "1 Japanese yen")]        // 0 minor units -> v=0 -> one
    [InlineData("en-US", "JPY", "2", "2 Japanese yen")]
    [InlineData("de-DE", "USD", "1", "1,00 US-Dollar")]
    [InlineData("de-DE", "USD", "2", "2,00 US-Dollar")]
    [InlineData("pl-PL", "USD", "5", "5,00 dolara amerykańskiego")]
    [InlineData("fr-FR", "USD", "1", "1,00 dollar des États-Unis")]
    public void Render_FullName_MatchesOracle(string culture, string code, string value, string expected)
    {
        CultureInfo c = Culture(culture);
        var digits = CurrencyDisplay.Digits(code);
        var result = CurrencyNameRenderer.Render(decimal.Parse(value, CultureInfo.InvariantCulture), code, c, digits, digits, true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_Negative_SignsTheNumber()
    {
        // oracle: en "currency/USD unit-width-full-name" -1234.56
        var result = CurrencyNameRenderer.Render(-1234.56m, "USD", Culture("en-US"), 2, 2, true);
        Assert.Equal("-1,234.56 US dollars", result);
    }
}
