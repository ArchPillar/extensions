using ArchPillar.Extensions.Localization;

namespace Acme.WasmGreeting;

/// <summary>
/// A trivial shared-library service whose user-facing string is localized. It exists so the WebAssembly sample
/// can reference a second localized assembly and prove that the build gathers and merges the referenced
/// library's catalogs alongside the app's own.
/// </summary>
public sealed class Greeter
{
    /// <summary>The greeting, in the active UI culture (English in code, German from the shipped catalog).</summary>
    public static string Greeting => Localizer.For<Greeter>().Translate("greeting", "Hello from the shared library");
}
