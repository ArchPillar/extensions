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
    /// Computes the CLDR plural <see cref="PluralOperands"/> for <paramref name="value"/> as displayed with
    /// <paramref name="visibleFractionDigits"/> fraction digits. The visible-digit count is supplied by the
    /// caller (the number formatter), so plural selection agrees with what is rendered rather than inferring
    /// precision from the value's own scale.
    /// </summary>
    /// <param name="value">The value to analyze.</param>
    /// <param name="visibleFractionDigits">The number of fraction digits the value is displayed with.</param>
    /// <param name="exponent">The CLDR compact-decimal exponent (the <c>e</c>/<c>c</c> operand): the power of
    /// ten of the divisor a compact formatter scaled <paramref name="value"/> by, or <c>0</c> for a value that
    /// is not compact-formatted (the default, and the only value the standard number/message path ever
    /// passes).</param>
    /// <returns>The operands for <paramref name="value"/> at that display precision.</returns>
    public static PluralOperands Operands(decimal value, int visibleFractionDigits, int exponent = 0)
    {
        var absolute = Math.Abs(value);
        var text = absolute.ToString(
            "F" + visibleFractionDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var separator = text.IndexOf('.');
#if NETSTANDARD2_0
        var integerText = separator < 0 ? text : text.Substring(0, separator);
        var fractionText = separator < 0 ? string.Empty : text.Substring(separator + 1);
#else
        var integerText = separator < 0 ? text : text[..separator];
        var fractionText = separator < 0 ? string.Empty : text[(separator + 1)..];
#endif
        var trimmed = fractionText.TrimEnd('0');

        // The i/f/t operands are 64-bit, but a decimal can carry up to 29 digits, so parsing a long run of
        // digits would overflow. Clamp to a fitting width: the low-order digits of the integer part (CLDR
        // rules use it through small moduli, and the rare value large enough to overflow resolves to "other"
        // anyway) and the leading digits of the fraction. The visible-digit counts v and w are unaffected.
        var i = ParseDigits(integerText, keepLowOrder: true);
        var v = fractionText.Length;
        var w = trimmed.Length;
        var f = v == 0 ? 0L : ParseDigits(fractionText, keepLowOrder: false);
        var t = w == 0 ? 0L : ParseDigits(trimmed, keepLowOrder: false);
        return new PluralOperands(absolute, i, v, w, f, t, exponent);
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

    /// <summary>
    /// Returns the gettext <c>Plural-Forms</c> header value (<c>nplurals=N; plural=EXPR;</c>) for a culture,
    /// derived from its CLDR cardinal rules. The C expression maps each <c>n</c> to the index of its form in
    /// <see cref="GettextOrder"/>, so an external gettext tool selects the right <c>msgstr[n]</c>.
    /// </summary>
    /// <param name="culture">The BCP-47 culture name.</param>
    /// <returns>The <c>Plural-Forms</c> header value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public static string GettextPluralForms(string culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return GettextPluralExpression.Build(GettextOrder(culture), RulesFor(CldrPluralData.Cardinal, culture));
    }

    // Parses a run of digits into a long that cannot overflow: a value with more than 18 digits is far
    // beyond any real plural count, so it is clamped to 18 digits — the low-order ones for the integer part
    // (to keep small moduli correct) and the high-order ones for a fraction.
    private static long ParseDigits(string digits, bool keepLowOrder)
    {
        const int MaxDigits = 18;
        if (digits.Length > MaxDigits)
        {
#if NETSTANDARD2_0
            digits = keepLowOrder ? digits.Substring(digits.Length - MaxDigits) : digits.Substring(0, MaxDigits);
#else
            digits = keepLowOrder ? digits[(digits.Length - MaxDigits)..] : digits[..MaxDigits];
#endif
        }

        return digits.Length == 0 ? 0L : long.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static PluralCategory Resolve(
        IReadOnlyDictionary<string, CldrPluralRule[]> table,
        string culture,
        PluralOperands operands)
    {
        return Evaluate(RulesFor(table, culture), operands);
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
        foreach (CldrPluralRule rule in RulesFor(CldrPluralData.Cardinal, culture))
        {
            categories.Add(rule.Category);
        }

        return categories;
    }

    // Resolves the rule set for a culture from the table, falling back from the full name to its base language
    // (the part before the first '-'), or an empty set when neither is present. Allocation-lean: a base-language
    // substring is taken only on the fallback path; an exact-name hit allocates nothing.
    private static CldrPluralRule[] RulesFor(IReadOnlyDictionary<string, CldrPluralRule[]> table, string culture)
    {
        if (table.TryGetValue(culture, out CldrPluralRule[]? rules))
        {
            return rules;
        }

        var dash = culture.IndexOf('-');
        if (dash > 0)
        {
#if NETSTANDARD2_0
            var language = culture.Substring(0, dash);
#else
            var language = culture[..dash];
#endif
            if (table.TryGetValue(language, out rules))
            {
                return rules;
            }
        }

        return [];
    }
}
