using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class CldrCompactDataTests
{
    [Fact]
    public void AllPinnedCompactPatterns_Parse()
    {
        foreach (CompactLocaleData data in CldrCompactData.Locales.Values)
        {
            AssertSetParses(data.ShortDecimal);
            AssertSetParses(data.LongDecimal);
            AssertSetParses(data.ShortCurrency);
            AssertSetParses(data.ShortCurrencyAlpha);
        }
    }

    [Fact]
    public void Locales_ContainsRootFallback()
    {
        Assert.True(CldrCompactData.Locales.ContainsKey("root"));
    }

    private static void AssertSetParses(CompactBucketSet? set)
    {
        if (set is null)
        {
            return;
        }

        foreach (CompactBucket bucket in set.Buckets)
        {
            foreach (CompactVariant variant in bucket.Variants)
            {
                // Skip the "0" no-compact sentinel and CLDR explicit-value literals (e.g. "mille"), which
                // carry no digit placeholder and are emitted verbatim rather than parsed.
                if (variant.Pattern == "0"
                    || (variant.Pattern.IndexOf('0') < 0 && variant.Pattern.IndexOf('#') < 0))
                {
                    continue;
                }

                NumberPattern parsed = NumberPatternParser.Parse(variant.Pattern);
                Assert.NotNull(parsed);
            }
        }
    }
}
