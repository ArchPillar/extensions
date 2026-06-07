using System.Globalization;
using ArchPillar.Extensions.Localization;
using Localization.TodoSample;

// English ships in code; German and French load from Translations/*.arb (scoped to the TodoStrings
// category). A PseudoLocalizationSource adds a "qps-ploc" QA culture that X's every translatable string,
// so anything that stays readable under it is NOT going through the localizer.
using var localizer = new Localizer(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en",
    Sources = [new PseudoLocalizationSource("qps-ploc")]
});

var strings = new TodoStrings(new LocalizerFactory(localizer).Create<TodoStrings>());

// A fixed to-do list. The task titles and the checkbox glyph are deliberately hardcoded (not
// translatable), so the pseudo run shows them unchanged — that is the point of the smoke test.
(string Title, bool Done)[] items =
[
    ("Buy milk", true),
    ("Write the report", false),
    ("Call Ada", false)
];

foreach (var culture in new[] { "en", "de", "fr", "qps-ploc" })
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    Console.WriteLine();
    Console.WriteLine($"===== {culture} =====");
    Console.WriteLine(strings.Title);
    Console.WriteLine(strings.Remaining(items.Count(item => !item.Done)));
    foreach (var (title, done) in items)
    {
        Console.WriteLine($"  [{(done ? "x" : " ")}] {title}");
    }

    Console.WriteLine(strings.AddHint);
}
