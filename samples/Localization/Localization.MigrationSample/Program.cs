using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MigrationSample;
using static ArchPillar.Extensions.Localization.TranslationMarkers;

// This sample shows migrating an app that already localizes with IStringLocalizer + .resx onto ArchPillar,
// WITHOUT rewriting call sites. The existing ResourceManager keeps serving its translations; ArchPillar's
// store layers on top and wins where it has an entry.

var services = new ServiceCollection();

// A real host registers logging; the ResourceManager localizer factory depends on it.
services.AddLogging();

// 1) The app's EXISTING setup: ResourceManager-backed IStringLocalizer over Resources/Greeting[.de].resx.
services.AddLocalization(options => options.ResourcesPath = "Resources");

// 2) Adopt ArchPillar. Its IStringLocalizer adapter COMPOSES over the ResourceManager factory registered
//    above: ambient hit wins, otherwise it falls through to the existing .resx, otherwise the name. The
//    ambient store loads the new German translation from Translations/de.arb (files-by-default), with no
//    call-site changes.
services.AddArchPillarLocalization();

using ServiceProvider provider = services.BuildServiceProvider();
IStringLocalizer<Greeting> localizer = provider.GetRequiredService<IStringLocalizer<Greeting>>();

foreach (var culture in new[] { "en", "de" })
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    Console.WriteLine($"--- {culture} ---");

    // "Welcome": ArchPillar's de.arb overrides the legacy .resx — the ambient entry wins.
    Console.WriteLine("Welcome: " + localizer["Welcome"]);

    // "Goodbye": only the legacy .resx has it — the adapter falls through, so the existing translation is kept.
    Console.WriteLine("Goodbye: " + localizer["Goodbye"]);

    // "Help": neither store has it — the name comes back (the in-code default / terminal fallback).
    Console.WriteLine("Help:    " + localizer["Help"]);
}

// A string that never flows through a localizer (a log line, an exception). L(...) returns it unchanged at
// runtime but marks it for extraction so translators still see it — the cheap escape hatch during migration.
Console.WriteLine();
Console.WriteLine(L("Migration complete."));
