using System.Globalization;
using ArchPillar.Extensions.Localization;
using Localization.TodoSample;

// ---------------------------------------------------------------------------
// Localization.TodoSample
//
// Demonstrates ArchPillar.Extensions.Localization in a no-DI console app:
//   - A self-scoped Localized<T> string bundle (member name is the key, type is the category)
//   - In-code English overridden by German and French .arb catalogs beside the binary
//   - ICU plurals ({count, plural, ...}) resolved per culture
//   - A pseudo-localization QA pass (qps-ploc) that X's translatable strings to catch hardcoded text
//
// The string bundle lives in TodoStrings.cs; the catalogs are Translations/de.arb and fr.arb.
// ---------------------------------------------------------------------------
using var store = new CatalogStore(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en",
    Sources = [new PseudoLocalizationSource("qps-ploc")]
});
var localizer = new Localizer(store);

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
