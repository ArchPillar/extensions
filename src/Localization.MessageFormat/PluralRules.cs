using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Resolves CLDR plural categories for a culture and value, using plural-rule data embedded from a
/// pinned Unicode CLDR release. This fills the gap left by <see cref="System.Globalization"/>, which
/// does not expose CLDR plural categories.
/// </summary>
public static class PluralRules
{
    private static readonly PluralCategory[] _canonicalOrder =
    [
        PluralCategory.Zero,
        PluralCategory.One,
        PluralCategory.Two,
        PluralCategory.Few,
        PluralCategory.Many,
        PluralCategory.Other
    ];

    /// <summary>
    /// Gets the Unicode CLDR version the embedded plural-rule data was generated from.
    /// </summary>
    public static string CldrVersion => CldrPluralData.CldrVersion;

    /// <summary>
    /// Resolves the cardinal plural category for <paramref name="operands"/> in
    /// <paramref name="culture"/> (used by <c>plural</c>).
    /// </summary>
    /// <param name="culture">The BCP-47 culture name. Unknown cultures fall back to their base language, then to <see cref="PluralCategory.Other"/>.</param>
    /// <param name="operands">The operands of the value being pluralized.</param>
    /// <returns>The resolved cardinal category.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public static PluralCategory Cardinal(string culture, PluralOperands operands)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return Resolve(CldrPluralData.Cardinal, culture, operands);
    }

    /// <summary>
    /// Resolves the ordinal plural category for <paramref name="operands"/> in
    /// <paramref name="culture"/> (used by <c>selectordinal</c>).
    /// </summary>
    /// <param name="culture">The BCP-47 culture name. Unknown cultures fall back to their base language, then to <see cref="PluralCategory.Other"/>.</param>
    /// <param name="operands">The operands of the value being pluralized.</param>
    /// <returns>The resolved ordinal category.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public static PluralCategory Ordinal(string culture, PluralOperands operands)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return Resolve(CldrPluralData.Ordinal, culture, operands);
    }

    /// <summary>
    /// Computes the CLDR plural <see cref="PluralOperands"/> for <paramref name="value"/>. The number
    /// of visible fraction digits is taken from the value's own scale (which a <see cref="decimal"/>
    /// preserves, including trailing zeros) and may be raised by <paramref name="minFractionDigits"/>.
    /// </summary>
    /// <param name="value">The value to analyze.</param>
    /// <param name="minFractionDigits">An optional minimum number of visible fraction digits to assume.</param>
    /// <returns>The operands for <paramref name="value"/>.</returns>
    public static PluralOperands Operands(decimal value, int? minFractionDigits = null)
    {
        var absolute = Math.Abs(value);
        var segments = absolute.ToString(CultureInfo.InvariantCulture).Split('.');
        var integerText = segments[0];
        var fractionText = segments.Length > 1 ? segments[1] : string.Empty;
        if (minFractionDigits is int minimum && minimum > fractionText.Length)
        {
            fractionText = fractionText.PadRight(minimum, '0');
        }

        var trimmed = fractionText.TrimEnd('0');
        var i = long.Parse(integerText, CultureInfo.InvariantCulture);
        var v = fractionText.Length;
        var w = trimmed.Length;
        var f = v == 0 ? 0L : long.Parse(fractionText, CultureInfo.InvariantCulture);
        var t = w == 0 ? 0L : long.Parse(trimmed, CultureInfo.InvariantCulture);
        return new PluralOperands(absolute, i, v, w, f, t, 0, 0);
    }

    /// <summary>
    /// Returns the plural categories a language uses, in the canonical CLDR order (with
    /// <see cref="PluralCategory.Other"/> last). This is the ordering the Portable Object provider maps
    /// onto gettext <c>msgstr[n]</c> indices.
    /// </summary>
    /// <param name="culture">The BCP-47 culture name.</param>
    /// <returns>The ordered categories the language uses for cardinals.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<PluralCategory> GettextOrder(string culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        HashSet<PluralCategory> used = CategoriesFor(culture);
        var order = new List<PluralCategory>();
        foreach (PluralCategory category in _canonicalOrder)
        {
            if (category == PluralCategory.Other || used.Contains(category))
            {
                order.Add(category);
            }
        }

        return order;
    }

    private static PluralCategory Resolve(
        IReadOnlyDictionary<string, CldrPluralRule[]> table,
        string culture,
        PluralOperands operands)
    {
        foreach (var candidate in CultureCandidates(culture))
        {
            if (table.TryGetValue(candidate, out CldrPluralRule[]? rules))
            {
                return Evaluate(rules, operands);
            }
        }

        return PluralCategory.Other;
    }

    private static PluralCategory Evaluate(CldrPluralRule[] rules, PluralOperands operands)
    {
        foreach (CldrPluralRule rule in rules)
        {
            if (CldrRuleEvaluator.Matches(rule.Condition, operands))
            {
                return rule.Category;
            }
        }

        return PluralCategory.Other;
    }

    private static HashSet<PluralCategory> CategoriesFor(string culture)
    {
        var categories = new HashSet<PluralCategory>();
        foreach (var candidate in CultureCandidates(culture))
        {
            if (CldrPluralData.Cardinal.TryGetValue(candidate, out CldrPluralRule[]? rules))
            {
                foreach (CldrPluralRule rule in rules)
                {
                    categories.Add(rule.Category);
                }

                break;
            }
        }

        return categories;
    }

    private static IEnumerable<string> CultureCandidates(string culture)
    {
        yield return culture;
        var parts = culture.Split('-');
        if (parts.Length > 1)
        {
            yield return parts[0];
        }
    }
}
