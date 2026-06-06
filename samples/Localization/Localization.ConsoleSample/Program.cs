using System.Globalization;
using ArchPillar.Extensions.Localization;

// The app ships English in code. A German catalog (Translations/de.arb) is copied beside the binary
// and loaded as an override at runtime — English needs no file because the in-code default is the
// source of truth and terminal fallback.
using var localizer = new Localizer(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en"
});

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
