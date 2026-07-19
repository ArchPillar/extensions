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

    [Fact]
    public void Resolve_SameCode_ReturnsCachedInstance()
    {
        // "zzy" is genuinely lower-case and unmatched: the fallback path computes code.ToUpperInvariant(),
        // which allocates a NEW string every time an actual case change happens. Two calls returning the
        // SAME string reference proves the result came from the cache, not a fresh Lookup each time.
        (var first, _) = CurrencyLookup.Resolve("zzy");
        (var second, _) = CurrencyLookup.Resolve("zzy");

        Assert.Same(first, second);
    }
}
