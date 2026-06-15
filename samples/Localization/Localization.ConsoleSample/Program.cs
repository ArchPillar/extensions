using System.Globalization;
using ArchPillar.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ---------------------------------------------------------------------------
// Localization.ConsoleSample
//
// Demonstrates ArchPillar.Extensions.Localization in a generic-host console app:
//   - Registering the DefaultLocalizer in DI with AddArchPillarLocalization and resolving it as a service
//   - In-code English default overridden at runtime by a German .xliff catalog beside the binary
//   - Named arguments ({name}) and ICU plurals ({count, plural, ...}) across both cultures
//   - English needs no file: the in-code default is the source of truth and the terminal fallback
//
// Everything lives in this file; the German catalog is Translations/de.xliff.
// ---------------------------------------------------------------------------
using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => services.AddArchPillarLocalization(new LocalizerOptions
    {
        TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
        SourceCulture = "en"
    }))
    .Build();

var localizer = host.Services.GetRequiredService<DefaultLocalizer>();

foreach (var culture in new[] { "en", "de" })
{
    CultureInfo target = CultureInfo.GetCultureInfo(culture);
    Console.WriteLine($"--- {culture} ---");
    Console.WriteLine(localizer.Translate(
        target, "home.greeting", "Hello {name}", context: null, ("name", "Ada")));

    for (var count = 0; count <= 2; count++)
    {
        Console.WriteLine(localizer.Translate(
            target,
            "inbox.count",
            "{count, plural, =0 {No messages} one {# message} other {# messages}}",
            context: null,
            ("count", count)));
    }
}
