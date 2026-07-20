using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CurrencyDataTests
{
    [Theory]
    [InlineData("JPY", 0)]
    [InlineData("BHD", 3)]
    [InlineData("USD", 2)]   // absent from the table -> default
    [InlineData("ZZZ", 2)]   // unknown -> default
    public void Digits_MatchCldr(string code, int expected) => Assert.Equal(expected, CurrencyData.Digits(code));

    [Fact]
    public void TryEntry_EnUsd_HasDollarSymbol()
    {
        Assert.True(CurrencyData.TryEntry("en", "USD", out CurrencyData.CurrencyEntry entry));
        Assert.Equal("$", entry.Symbol);
        Assert.Equal("US dollars", entry.Names["other"]);
    }

    [Fact]
    public void TryEntry_EnSek_HasDisambiguatedSymbolAndNarrow()
    {
        Assert.True(CurrencyData.TryEntry("en", "SEK", out CurrencyData.CurrencyEntry entry));
        Assert.Equal("SEK", entry.Symbol);
        Assert.Equal("kr", entry.Narrow);
    }

    [Fact]
    public void Spacing_En_IsNoBreakSpace() => Assert.Equal("\u00A0", CurrencyData.Spacing("en"));

    [Fact]
    public void UnitPatterns_En_IsBraceZeroSpaceBraceOne() =>
        Assert.Equal("{0} {1}", CurrencyData.UnitPatterns("en")!["other"]);
}
