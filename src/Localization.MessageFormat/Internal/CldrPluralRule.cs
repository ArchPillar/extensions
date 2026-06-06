namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// A single CLDR plural rule: the <see cref="PluralCategory"/> it selects and the CLDR condition
/// expression that must hold for it to apply. Rules are evaluated in order; the first match wins.
/// </summary>
/// <param name="Category">The category this rule selects.</param>
/// <param name="Condition">The CLDR condition expression (for example <c>i = 1 and v = 0</c>).</param>
internal readonly record struct CldrPluralRule(PluralCategory Category, string Condition);
