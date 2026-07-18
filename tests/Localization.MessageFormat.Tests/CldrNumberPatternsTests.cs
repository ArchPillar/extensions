using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CldrNumberPatternsTests
{
    [Fact]
    public void Locales_ContainRootAndFixtureLocales()
    {
        Assert.Equal("48", CldrNumberPatterns.CldrVersion);
        foreach (var locale in new[] { "root", "en", "de", "fr", "nl", "ja", "hi" })
        {
            Assert.True(CldrNumberPatterns.Locales.ContainsKey(locale), $"missing locale '{locale}'");
        }
    }

    [Fact]
    public void EnglishPatterns_MatchCldrStandard()
    {
        CldrNumberPatternSet en = CldrNumberPatterns.Locales["en"];

        Assert.Equal("#,##0.###", en.Decimal);
        Assert.Equal("¤#,##0.00", en.Currency);
        Assert.StartsWith("#,##0", en.Percent, StringComparison.Ordinal);
        Assert.EndsWith("%", en.Percent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("hi")]
    public void PatternGrouping_AgreesWithCultureGroupSizes(string locale)
    {
        // E2/E4: grouping positions are an NFI atom; this cross-checks the pattern's own grouping
        // against the culture data once, for the fixture locales (the generation-time check of the spec).
        CldrNumberPatternSet set = CldrNumberPatterns.Locales[locale];
        var integerPart = set.Decimal.Split('.')[0];
        var groups = integerPart.Split(',');
        var culture = CultureInfo.GetCultureInfo(locale);

        // primary group = length of the last segment; secondary = the one before it (when present).
        var primary = groups[^1].Length;
        Assert.Equal(culture.NumberFormat.NumberGroupSizes[0], primary);
        if (groups.Length > 2)
        {
            var secondary = groups[^2].Length;
            var sizes = culture.NumberFormat.NumberGroupSizes;
            Assert.Equal(sizes.Length > 1 ? sizes[1] : sizes[0], secondary);
        }
    }

    [Fact]
    public void AllPinnedPatterns_Parse()
    {
        foreach (CldrNumberPatternSet set in CldrNumberPatterns.Locales.Values)
        {
            foreach (var pattern in new[] { set.Decimal, set.Percent, set.Currency })
            {
                Exception? error = Record.Exception(() => NumberPatternParser.Parse(pattern));
                Assert.True(error is null, $"pattern '{pattern}' failed to parse: {error?.Message}");
            }
        }
    }
}
