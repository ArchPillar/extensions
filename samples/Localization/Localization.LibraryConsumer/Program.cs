using System.Globalization;
using Acme.Greeting;

// There is NO localization setup here — no DI, no AddArchPillarLocalization, no files placed anywhere. The
// Acme.Greeting library ships its German translations embedded in its own DLL (as a culture satellite),
// and the ambient store discovers them lazily the first time German is requested. Batteries included.
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
