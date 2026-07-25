using System.Globalization;
using ArchPillar.Extensions.Localization;
using Localization.TodoSample;

// ---------------------------------------------------------------------------
// Localization.TodoSample
//
// Demonstrates ArchPillar.Extensions.Localization in a no-DI console app:
//   - A self-scoped Localized<T> string bundle (member name is the key, type is the category)
//   - In-code English overridden by German and French .xliff catalogs beside the binary
//   - ICU plurals ({count, plural, ...}) resolved per culture
//
// The string bundle lives in TodoStrings.cs; the catalogs are Translations/de.xliff and fr.xliff.
// ---------------------------------------------------------------------------
using var context = new LocalizationContext(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en"
});

var strings = new TodoStrings(context.For<TodoStrings>());

// A fixed to-do list. The task titles and the checkbox glyph are deliberately hardcoded (not translatable).
(string Title, bool Done)[] items =
[
    ("Buy milk", true),
    ("Write the report", false),
    ("Call Ada", false)
];

foreach (var culture in new[] { "en", "de", "fr" })
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
