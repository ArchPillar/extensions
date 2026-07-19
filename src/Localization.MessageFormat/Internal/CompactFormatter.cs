using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Applies ICU compact notation for EVERY value in a locale/unit, byte-matching <c>Intl.NumberFormat</c>.
/// A value that reaches a real magnitude bucket scales into it, rounds to compact precision, selects the
/// plural-count pattern, and composes the suffix through the Spec 2
/// <see cref="NumberPatternParser"/>/<see cref="PatternRenderer"/> engine. A value below the smallest bucket
/// (or on a CLDR "no-compact" sentinel bucket, e.g. German thousands) renders PLAIN through the standard
/// pattern with the compact 0/1 fraction and ECMA-402 min2 grouping. Rounding may carry a value up one
/// bucket. Owns only the compact-specific arithmetic; all affix composition is the engine's. Returns
/// <see langword="null"/> only when the locale/unit has no compact set at all (unreachable — root always
/// has data), so the caller falls through to standard formatting.
/// </summary>
internal static class CompactFormatter
{
    public static string? TryFormat(
        decimal value,
        NumberNotation notation,
        NumberUnit unit,
        CultureInfo culture,
        string currencySymbol,
        string currencyCode,
        bool grouping)
    {
        var absolute = Math.Abs(value);
        CompactBucketSet? set = SetFor(culture.Name, unit, notation, currencySymbol);
        if (set is null || set.Buckets.Count == 0)
        {
            return null;
        }

        var index = SelectBucket(set, absolute);
        index = ResolveCarry(set, index, value);

        if (index < 0 || IsSentinel(set.Buckets[index]))
        {
            // PLAIN render (no suffix): the value stays below the smallest real magnitude, or sits on a CLDR
            // "no-compact" sentinel bucket. Render through the standard pattern with the compact 0/1 fraction
            // and min2 grouping — matching Intl for every non-compacted value.
            var plainFraction = absolute < 10m ? 1 : 0;
            var plainRounded = Math.Round(value, plainFraction, MidpointRounding.AwayFromZero);
            return RenderPlain(plainRounded, plainFraction, unit, culture, currencySymbol, currencyCode, grouping);
        }

        // REAL bucket -> compact with suffix. ResolveCarry already fixed the final index, so recompute the
        // scaled value and fraction once for it.
        CompactBucket bucket = set.Buckets[index];
        var divisor = DivisorFor(bucket);
        var scaled = value / divisor;
        var fractionDigits = Math.Abs(scaled) < 10m ? 1 : 0;
        var rounded = Math.Round(scaled, fractionDigits, MidpointRounding.AwayFromZero);

        var visibleFraction = VisibleFraction(rounded, fractionDigits);
        PluralCategory category = PluralRules.Cardinal(culture.Name, PluralRules.Operands(rounded, visibleFraction));
        var pattern = SelectPattern(bucket, category, rounded);
        if (!HasDigitBody(pattern))
        {
            // A CLDR explicit-value pattern with no digit placeholder (e.g. French "mille" for exactly 1000):
            // emit the literal verbatim, minus-prefixed for negatives. No number is substituted.
            return value < 0m ? culture.NumberFormat.NegativeSign + pattern : pattern;
        }

        NumberPattern parsed = NumberPatternParser.Parse(ReduceZeroRun(pattern));
        return PatternRenderer.Render(
            parsed,
            rounded,
            PatternPrecision.Fraction(0, fractionDigits),
            grouping: false,
            culture,
            currencySymbol,
            currencyCode);
    }

    // Rounds at the current level and advances one bucket when the rounded value reaches the next bucket's
    // magnitude — the single carry owner for both the plain level (idx < 0 or a sentinel bucket) and a real
    // bucket. 999.9 -> 1000 enters bucket 0; de 999999.9 -> 1000000 enters the Mio. bucket. One carry max.
    private static int ResolveCarry(CompactBucketSet set, int index, decimal value)
    {
        if (index < 0 || IsSentinel(set.Buckets[index]))
        {
            var fraction = Math.Abs(value) < 10m ? 1 : 0;
            var rounded = Math.Abs(Math.Round(value, fraction, MidpointRounding.AwayFromZero));
            var nextIndex = index + 1;               // idx < 0 -> 0 (first bucket); sentinel idx -> idx + 1
            if (nextIndex < set.Buckets.Count && rounded >= set.Buckets[nextIndex].Magnitude)
            {
                return nextIndex;
            }

            return index;
        }

        CompactBucket bucket = set.Buckets[index];
        var divisor = DivisorFor(bucket);
        var scaled = value / divisor;
        var frac = Math.Abs(scaled) < 10m ? 1 : 0;
        var roundedScaled = Math.Round(scaled, frac, MidpointRounding.AwayFromZero);
        if (index + 1 < set.Buckets.Count && Math.Abs(roundedScaled) * divisor >= set.Buckets[index + 1].Magnitude)
        {
            return index + 1;
        }

        return index;
    }

    // A bucket is a CLDR "no-compact" sentinel when its Other-category pattern is the bare literal "0"
    // (e.g. German short-decimal below a million): the magnitude exists but carries no abbreviation.
    private static bool IsSentinel(CompactBucket bucket) =>
        SelectCategoryPattern(bucket, PluralCategory.Other) == "0";

    // Renders a non-compacted value through the standard pattern: the compact 0/1 fraction overrides the
    // pattern's own fraction body (so a currency ".00" drops to compact minor-units) while the pattern still
    // supplies the affixes (currency symbol + CLDR joiner). Grouping is ECMA-402 min2.
    private static string RenderPlain(
        decimal rounded,
        int fraction,
        NumberUnit unit,
        CultureInfo culture,
        string currencySymbol,
        string currencyCode,
        bool grouping)
    {
        NumberPattern pattern = StandardPatterns.For(unit, culture);
        var group = grouping && Min2Grouping(rounded, culture.NumberFormat);
        return PatternRenderer.Render(
            pattern, rounded, PatternPrecision.Fraction(0, fraction), group, culture, currencySymbol, currencyCode);
    }

    // ECMA-402 "min2" grouping (compact notation's default): group only when the integer part has at least
    // primaryGroupSize + 2 digits — i.e. the most-significant group would be preceded by >= 2 grouped digits.
    // For standard 3-digit grouping this leaves 4-digit values ungrouped (1234) but groups 5+ (12345).
    private static bool Min2Grouping(decimal value, NumberFormatInfo format)
    {
        var groupSizes = format.NumberGroupSizes;
        if (groupSizes.Length == 0 || groupSizes[0] == 0)
        {
            return false;
        }

        return IntegerDigitCount(Math.Abs(value)) - groupSizes[0] >= 2;
    }

    private static int IntegerDigitCount(decimal absolute)
    {
        var text = absolute.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? text.Length : dot;
    }

    // The set for a unit+notation, walking the locale fallback chain (exact -> base language -> root) and
    // returning the first non-null set. Currency always uses the short-currency set (CLDR has no long
    // currency compact); an alphabetic symbol prefers the alphaNextToNumber variant when present.
    private static CompactBucketSet? SetFor(string locale, NumberUnit unit, NumberNotation notation, string currencySymbol)
    {
        if (unit == NumberUnit.Currency)
        {
            if (IsAlphabeticSymbol(currencySymbol))
            {
                CompactBucketSet? alpha = FirstNonNullSet(locale, data => data.ShortCurrencyAlpha);
                if (alpha is not null)
                {
                    return alpha;
                }
            }

            return FirstNonNullSet(locale, data => data.ShortCurrency);
        }

        return notation == NumberNotation.CompactLong
            ? FirstNonNullSet(locale, data => data.LongDecimal)
            : FirstNonNullSet(locale, data => data.ShortDecimal);
    }

    // A symbol is "alphabetic at the digit boundary" when either boundary character is a Unicode letter
    // (the ISO-code fallback "USD", or letter symbols like "kr"). Pure-glyph symbols ($/€/¥) are not.
    private static bool IsAlphabeticSymbol(string symbol)
    {
        if (symbol.Length == 0)
        {
            return false;
        }

#if NETSTANDARD2_0
        var last = symbol[symbol.Length - 1];
#else
        var last = symbol[^1];
#endif
        return char.IsLetter(symbol[0]) || char.IsLetter(last);
    }

    private static CompactBucketSet? FirstNonNullSet(
        string locale, Func<CompactLocaleData, CompactBucketSet?> pick)
    {
        if (CldrCompactData.Locales.TryGetValue(locale, out CompactLocaleData? exact) && pick(exact) is { } exactSet)
        {
            return exactSet;
        }

        var dash = locale.IndexOf('-');
        if (dash > 0)
        {
#if NETSTANDARD2_0
            var language = locale.Substring(0, dash);
#else
            var language = locale[..dash];
#endif
            if (CldrCompactData.Locales.TryGetValue(language, out CompactLocaleData? baseData) && pick(baseData) is { } baseSet)
            {
                return baseSet;
            }
        }

        return CldrCompactData.Locales.TryGetValue("root", out CompactLocaleData? root) ? pick(root) : null;
    }

    // The greatest bucket index whose magnitude is <= absolute, or -1 when absolute is below the smallest
    // bucket (the plain-render level). Buckets are ascending, so the scan stops at the first larger magnitude.
    private static int SelectBucket(CompactBucketSet set, decimal absolute)
    {
        var index = -1;
        for (var i = 0; i < set.Buckets.Count; i++)
        {
            if (set.Buckets[i].Magnitude <= absolute)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    // divisor = magnitude / 10^(zeroCount - 1). zeroCount is the number of '0' characters in the bucket's
    // representative pattern (all count-variants of a bucket share the same zero-count).
    private static decimal DivisorFor(CompactBucket bucket)
    {
        var zeroCount = SampleZeroCount(bucket);
        return bucket.Magnitude / Pow10(zeroCount - 1);
    }

    private static int SampleZeroCount(CompactBucket bucket)
    {
        var pattern = SelectCategoryPattern(bucket, PluralCategory.Other);
        var count = 0;
        foreach (var c in pattern)
        {
            if (c == '0')
            {
                count++;
            }
        }

        return count;
    }

    // Selects the pattern for the compacted value: a CLDR explicit-value variant whose value equals the
    // rounded compacted number wins (TR35, e.g. French "mille" at exactly 1000); otherwise the plural-category
    // pattern.
    private static string SelectPattern(CompactBucket bucket, PluralCategory category, decimal rounded)
    {
        // The explicit-value match is on MAGNITUDE — like bucket selection (uses `absolute`) and the plural
        // operands (PluralRules does Math.Abs). ExplicitValue is always positive but `rounded` is signed, so
        // compare against its absolute value; otherwise a negative value (fr -1000 compact-long) would miss
        // its explicit "mille" literal and wrongly fall to the count-one pattern.
        var magnitude = Math.Abs(rounded);
        foreach (CompactVariant variant in bucket.Variants)
        {
            if (variant.ExplicitValue == magnitude)
            {
                return variant.Pattern;
            }
        }

        return SelectCategoryPattern(bucket, category);
    }

    // The plural-category pattern for a bucket, ignoring explicit-value variants: the matching category,
    // else Other, else the first category variant. Also the divisor source (SampleZeroCount) — an explicit
    // literal like "mille" carries no zero-count, so it must never drive the divisor.
    private static string SelectCategoryPattern(CompactBucket bucket, PluralCategory category)
    {
        string? other = null;
        string? first = null;
        foreach (CompactVariant variant in bucket.Variants)
        {
            if (variant.ExplicitValue is not null)
            {
                continue;
            }

            if (variant.Category == category)
            {
                return variant.Pattern;
            }

            if (variant.Category == PluralCategory.Other)
            {
                other = variant.Pattern;
            }

            first ??= variant.Pattern;
        }

        return other ?? first ?? bucket.Variants[0].Pattern;
    }

    // Whether a pattern carries a digit placeholder ('0'/'#'). Explicit-value literals like "mille" do not,
    // and are emitted verbatim rather than rendered through the digit engine.
    private static bool HasDigitBody(string pattern) =>
        pattern.IndexOf('0') >= 0 || pattern.IndexOf('#') >= 0;

    // Reduces the pattern's maximal run of '0' to a single '0' so the zero-count drives only the divisor,
    // not the minimum integer digits ("00K" -> "0K", "000 thousand" -> "0 thousand").
    private static string ReduceZeroRun(string pattern)
    {
        var start = pattern.IndexOf('0');
        if (start < 0)
        {
            return pattern;
        }

        var end = start;
        while (end + 1 < pattern.Length && pattern[end + 1] == '0')
        {
            end++;
        }

        if (end == start)
        {
            return pattern;
        }

#if NETSTANDARD2_0
        return pattern.Substring(0, start) + "0" + pattern.Substring(end + 1);
#else
        return string.Concat(pattern.AsSpan(0, start), "0", pattern.AsSpan(end + 1));
#endif
    }

    // The fraction digits the compacted value actually displays after trailing-zero trim (fractionDigits is
    // 0 or 1). Drives plural-operand selection so the category matches the rendered text (1.0M -> "1M" -> one).
    private static int VisibleFraction(decimal rounded, int fractionDigits)
    {
        if (fractionDigits == 0)
        {
            return 0;
        }

        return (Math.Abs(rounded) * 10m) % 10m == 0m ? 0 : 1;
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++)
        {
            result *= 10m;
        }

        return result;
    }
}
