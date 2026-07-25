namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// The ICU currency display width (ECMA-402 <c>currencyDisplay</c>): the <c>unit-width-*</c> skeleton
/// stems. Meaningful only for <see cref="NumberUnit.Currency"/>.
/// </summary>
internal enum CurrencyWidth
{
    /// <summary>Locale symbol (default; <c>unit-width-short</c>).</summary>
    Short,

    /// <summary>Narrow symbol (<c>unit-width-narrow</c>), falling back to the short symbol.</summary>
    Narrow,

    /// <summary>The ISO code (<c>unit-width-iso-code</c>).</summary>
    IsoCode,

    /// <summary>The plural display name (<c>unit-width-full-name</c>).</summary>
    FullName
}
