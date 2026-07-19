using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Formats a numeric value with an ICU number style — the same style syntax accepted in a
/// <c>{arg, number, X}</c> message placeholder — so a value presented on its own renders identically to the
/// same number inside a translated message. The culture defaults to <see cref="CultureInfo.CurrentUICulture"/>
/// (the culture the localizer renders translations in), deliberately NOT <see cref="CultureInfo.CurrentCulture"/>,
/// so loose values and in-message numbers stay in the same locale.
/// </summary>
public static class NumberLocalizationExtensions
{
    /// <summary>Formats <paramref name="value"/> with an ICU number <paramref name="style"/> in <paramref name="culture"/>.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="style">An ICU skeleton (for example <c>::currency/USD</c>), a named style (<c>integer</c>/<c>currency</c>/<c>percent</c>), or <see langword="null"/> for the default number format.</param>
    /// <param name="culture">The formatting culture, or <see langword="null"/> for <see cref="CultureInfo.CurrentUICulture"/>.</param>
    /// <returns>The formatted string.</returns>
    /// <exception cref="MessageFormatException"><paramref name="style"/> is not a valid number style or skeleton.</exception>
    public static string ToLocalizedString(this decimal value, string? style = null, CultureInfo? culture = null) =>
        NumberFormatting.Format(value, style, culture ?? CultureInfo.CurrentUICulture);

    /// <inheritdoc cref="ToLocalizedString(decimal, string, CultureInfo)"/>
    public static string ToLocalizedString(this double value, string? style = null, CultureInfo? culture = null) =>
        NumberFormatting.Format(value, style, culture ?? CultureInfo.CurrentUICulture);

    /// <inheritdoc cref="ToLocalizedString(decimal, string, CultureInfo)"/>
    public static string ToLocalizedString(this int value, string? style = null, CultureInfo? culture = null) =>
        NumberFormatting.Format(value, style, culture ?? CultureInfo.CurrentUICulture);

    /// <inheritdoc cref="ToLocalizedString(decimal, string, CultureInfo)"/>
    public static string ToLocalizedString(this long value, string? style = null, CultureInfo? culture = null) =>
        NumberFormatting.Format(value, style, culture ?? CultureInfo.CurrentUICulture);

    /// <summary>
    /// Formats any <see cref="IFormattable"/> value with an ICU number <paramref name="style"/> — the catch-all
    /// for numeric types without a dedicated overload. A value the engine can convert to a number is ICU-formatted;
    /// a value it cannot (for example a <see cref="System.Numerics.BigInteger"/>, which is not
    /// <see cref="IConvertible"/>) degrades to its own <see cref="IFormattable.ToString(string, IFormatProvider)"/>.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="style">An ICU skeleton, a named style, or <see langword="null"/> for the default number format.</param>
    /// <param name="culture">The formatting culture, or <see langword="null"/> for <see cref="CultureInfo.CurrentUICulture"/>.</param>
    /// <returns>The formatted string.</returns>
    /// <exception cref="MessageFormatException"><paramref name="style"/> is not a valid number style or skeleton.</exception>
    public static string ToLocalizedString(this IFormattable value, string? style = null, CultureInfo? culture = null) =>
        NumberFormatting.Format(value, style, culture ?? CultureInfo.CurrentUICulture);
}
