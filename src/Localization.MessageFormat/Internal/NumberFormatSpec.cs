namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>The kind of number a <see cref="NumberFormatSpec"/> renders.</summary>
internal enum NumberUnit
{
    /// <summary>A plain decimal number.</summary>
    Decimal,

    /// <summary>A percentage (value multiplied by 100 with the locale percent sign).</summary>
    Percent,

    /// <summary>A currency amount.</summary>
    Currency
}

/// <summary>
/// A resolved number-format intent: what unit to render, the currency code (when any), the fraction-digit
/// bounds (<see langword="null"/> means "unit default"), and whether to group. Produced from a message's
/// <c>{arg, number, X}</c> style by <c>NumberFormatting.Resolve</c>.
/// </summary>
/// <param name="Unit">The number kind.</param>
/// <param name="CurrencyCode">The ISO currency code for an explicit-currency skeleton, or <see langword="null"/>.</param>
/// <param name="MinFractionDigits">The minimum visible fraction digits, or <see langword="null"/> for the unit default.</param>
/// <param name="MaxFractionDigits">The maximum visible fraction digits, or <see langword="null"/> for the unit default.</param>
/// <param name="Grouping">Whether to insert grouping (thousands) separators.</param>
internal sealed record NumberFormatSpec(
    NumberUnit Unit,
    string? CurrencyCode,
    int? MinFractionDigits,
    int? MaxFractionDigits,
    bool Grouping)
{
    /// <summary>The default: a grouped decimal with up to three trailing-zero-trimmed fraction digits.</summary>
    public static NumberFormatSpec Default { get; } = new(NumberUnit.Decimal, null, null, null, true);

    /// <summary>The <c>integer</c> style: a grouped whole number.</summary>
    public static NumberFormatSpec Integer { get; } = new(NumberUnit.Decimal, null, 0, 0, true);

    /// <summary>The <c>percent</c> style.</summary>
    public static NumberFormatSpec Percent { get; } = new(NumberUnit.Percent, null, null, null, true);

    /// <summary>The <c>currency</c> style, optionally with an explicit ISO code (<see langword="null"/> = the culture's own).</summary>
    public static NumberFormatSpec Currency(string? code) => new(NumberUnit.Currency, code, null, null, true);
}
