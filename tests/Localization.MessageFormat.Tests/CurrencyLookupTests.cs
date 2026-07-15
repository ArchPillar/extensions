using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CurrencyLookupTests
{
    [Fact]
    public void Resolve_KnownCurrency_ReturnsSymbolAndCldrMinorUnits()
    {
        (var symbol, var digits) = CurrencyLookup.Resolve("USD");

        Assert.Equal("$", symbol);
        Assert.Equal(2, digits);
    }

    [Theory]
    [InlineData("JPY", 0)]   // yen: zero minor units
    [InlineData("BHD", 3)]   // Bahraini dinar: three
    [InlineData("USD", 2)]
    public void Resolve_MinorUnits_MatchCldr(string code, int expectedDigits)
    {
        (_, var digits) = CurrencyLookup.Resolve(code);

        Assert.Equal(expectedDigits, digits);
    }

    [Fact]
    public void Resolve_UnknownCode_FallsBackToCodeAndTwoDigits()
    {
        (var symbol, var digits) = CurrencyLookup.Resolve("ZZZ");

        Assert.Equal("ZZZ", symbol);
        Assert.Equal(2, digits);
    }
}
