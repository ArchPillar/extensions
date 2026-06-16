// Skill oracle runner for the `archpillar-localization` Agent Skill.
// Purpose/methodology: docs/localization/internals/llm-skill-testing.md
using System.Globalization;
using static ArchPillar.Extensions.Localization.Localizer;

Console.WriteLine("== runtime: defaults render with no catalogs ==");
Console.WriteLine("  " + Translate("greeting", "Hello {name}!", ("name", "Ada")));
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
Console.WriteLine("  en/1: " + Translate("inbox", "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 1)));
Console.WriteLine("  en/5: " + Translate("inbox", "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 5)));
