using System.Globalization;
using ArchPillar.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The app ships English in code. A German catalog (Translations/de.arb) is copied beside the binary and
// loaded as an override at runtime — English needs no file because the in-code default is the source of
// truth and terminal fallback. Here the Localizer is registered with the DI container and resolved like
// any other service; AddArchPillarLocalization works the same in a console host as it does in ASP.NET.
using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => services.AddArchPillarLocalization(new LocalizerOptions
    {
        TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
        SourceCulture = "en"
    }))
    .Build();

var localizer = host.Services.GetRequiredService<Localizer>();

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
