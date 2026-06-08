using System.Globalization;
using Acme.Greeting;

// ---------------------------------------------------------------------------
// Localization.LibraryConsumer
//
// Demonstrates ArchPillar.Extensions.Localization consuming a batteries-included library:
//   - Zero configuration — no DI, no AddArchPillarLocalization, no files placed on disk
//   - German resolved from the library's embedded culture satellite via the ambient store
//   - A localized exception message thrown with no services available (rules out DI)
//
// The library lives in the paired Localization.GreetingLibrary project (Greeter, Validator).
// ---------------------------------------------------------------------------
var greeter = new Greeter();
var validator = new Validator();

foreach (var culture in new[] { "en", "de" })
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    Console.WriteLine($"--- {culture} ---");

    // A normal library call — English from code, German from the library's embedded catalog.
    Console.WriteLine(greeter.Greet("Ada"));

    // A localized exception message, resolved with no services available.
    try
    {
        validator.ValidateRequired("Email", value: null);
    }
    catch (ArgumentException exception)
    {
        Console.WriteLine("validation: " + exception.Message);
    }
}
