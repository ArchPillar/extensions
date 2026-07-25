namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// One variant of a compact bucket: the pattern for a plural category, or — when <paramref name="ExplicitValue"/>
/// is non-null — a CLDR explicit-value pattern (e.g. French <c>"1"</c>→<c>"mille"</c>) selected when the
/// compacted value equals that number, ahead of any plural category (TR35).
/// </summary>
/// <param name="Category">The CLDR plural category this pattern applies to; unused for an explicit-value variant.</param>
/// <param name="ExplicitValue">The exact compacted value this pattern matches, or <see langword="null"/> for a plural-category variant.</param>
/// <param name="Pattern">The CLDR compact pattern (for example <c>0K</c>, <c>¤0K</c>, <c>0 thousand</c>, or the digit-less literal <c>mille</c>).</param>
internal readonly record struct CompactVariant(PluralCategory Category, decimal? ExplicitValue, string Pattern);

/// <summary>
/// The compact patterns for one magnitude (for example 1000), keyed by plural category. The number of
/// <c>0</c> characters in a variant's pattern carries the divisor (TR35); selection falls back to the
/// <see cref="PluralCategory.Other"/> variant when the computed category is absent.
/// </summary>
/// <param name="Magnitude">The power-of-ten threshold this bucket covers. A <see cref="decimal"/> (not
/// <see cref="long"/>) because CLDR carries magnitudes up to 10^19, which overflow <see cref="long"/>.</param>
/// <param name="Variants">The per-category patterns for this magnitude.</param>
internal sealed record CompactBucket(decimal Magnitude, IReadOnlyList<CompactVariant> Variants);

/// <summary>A locale's compact buckets for one notation, in ascending <see cref="CompactBucket.Magnitude"/>.</summary>
/// <param name="Buckets">The buckets, ordered by ascending magnitude.</param>
internal sealed record CompactBucketSet(IReadOnlyList<CompactBucket> Buckets);

/// <summary>
/// A locale's compact pattern sets. Any set is <see langword="null"/> when CLDR has no data for that
/// notation in the locale (the resolver then walks the locale fallback chain, ending at root).
/// </summary>
/// <param name="ShortDecimal">Short-form decimal compact patterns, or <see langword="null"/>.</param>
/// <param name="LongDecimal">Long-form decimal compact patterns, or <see langword="null"/>.</param>
/// <param name="ShortCurrency">Short-form currency compact patterns, or <see langword="null"/>.</param>
/// <param name="ShortCurrencyAlpha">The alphaNextToNumber short-currency variant, or <see langword="null"/>.</param>
internal sealed record CompactLocaleData(
    CompactBucketSet? ShortDecimal,
    CompactBucketSet? LongDecimal,
    CompactBucketSet? ShortCurrency,
    CompactBucketSet? ShortCurrencyAlpha);
