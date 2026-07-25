using System.Globalization;
using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.MessageFormat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ---------------------------------------------------------------------------
// Localization.ConsoleSample
//
// Demonstrates ArchPillar.Extensions.Localization in a generic-host console app:
//   - Registering the Localizer in DI with AddArchPillarLocalization and resolving ILocalizer as a service
//   - In-code English default overridden at runtime by a German .xliff catalog beside the binary
//   - Named arguments ({name}) and ICU plurals ({count, plural, ...}) across both cultures
//   - English needs no file: the in-code default is the source of truth and the terminal fallback
//   - A {..., number, ...} value in a message formats in the culture the string is rendered in: the
//     target culture when the string is translated (cart.total, in German), the source culture
//     otherwise; ToLocalizedString takes the culture explicitly, so it always formats in the given
//     culture — the reliable choice for a currency width or compact notation shown on its own
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

var localizer = host.Services.GetRequiredService<ILocalizer>();

foreach (var culture in new[] { "en", "de" })
{
    CultureInfo target = CultureInfo.GetCultureInfo(culture);
    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = target;
    Console.WriteLine($"--- {culture} ---");
    Console.WriteLine(localizer.Translate("home.greeting", "Hello {name}", ("name", "Ada")));

    for (var count = 0; count <= 2; count++)
    {
        Console.WriteLine(localizer.Translate(
            "inbox.count",
            "{count, plural, =0 {No messages} one {# message} other {# messages}}",
            ("count", count)));
    }

    // ToLocalizedString takes the culture explicitly, so these always format in `target` — the
    // reliable choice for a standalone value, regardless of whether a translation exists.
    Console.WriteLine($"Price:      {1234.56m.ToLocalizedString("::currency/USD", target)}");
    Console.WriteLine($"Full name:  {1234.56m.ToLocalizedString("::currency/USD unit-width-full-name", target)}");
    Console.WriteLine($"Compact:    {1234m.ToLocalizedString("::compact-short currency/USD", target)}");

    // In a message, the number formats in the culture the string renders in: the source culture for
    // the (untranslated) English default, the target culture once a translation exists — de.xliff
    // carries a German cart.total, so this line's number goes German only under --- de ---.
    Console.WriteLine(localizer.Translate(
        "cart.total", "Total: {amount, number, ::currency/USD}", ("amount", 1234.56m)));
}
