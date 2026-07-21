// Skill oracle runner for the `archpillar-localization` Agent Skill.
// Purpose/methodology: docs/localization/internals/llm-skill-testing.md
using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat;
using static ArchPillar.Extensions.Localization.Localizer;

Console.WriteLine("== runtime: defaults render with no catalogs ==");
Console.WriteLine("  " + Translate("greeting", "Hello {name}!", ("name", "Ada")));
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
Console.WriteLine("  en/1: " + Translate("inbox", "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 1)));
Console.WriteLine("  en/5: " + Translate("inbox", "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 5)));

Console.WriteLine();
LocOracle.Demo.Run();

// Behavior gate: Phase-A currency formatting. Asserts via ToLocalizedString, which takes an
// explicit culture, rather than the in-message default (the oracle has no `de` catalog, so an
// in-message `{...}` placeholder renders in the source culture (en), not `de` -- that is correct
// library behavior, not something to assert German against). Canonical value from
// tests/Localization.MessageFormat.Tests/CurrencyDisplayParityTests.cs:41.
Console.WriteLine();
Console.WriteLine("== gate: currency formatting (ToLocalizedString, culture-explicit) ==");
var germanCulture = CultureInfo.GetCultureInfo("de-DE");
var deTotal = 1234.56m.ToLocalizedString("::currency/USD", germanCulture);
if (deTotal == "1.234,56\u00A0$")
{
    Console.WriteLine("[PASS] de-DE currency render: '" + deTotal + "'");
}
else
{
    Console.WriteLine($"[FAIL] de-DE currency render: '{deTotal}'");
    Environment.Exit(1);
}
