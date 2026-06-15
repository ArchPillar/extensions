using System.Globalization;
using ArchPillar.Extensions.Localization;

// ---------------------------------------------------------------------------
// Localization.AotSample
//
// Demonstrates ArchPillar.Extensions.Localization in a NativeAOT app, the
// AOT-safe way:
//   - a loose .xliff file copied beside the binary, loaded by the ambient store
//   - a catalog embedded in the main assembly (advertised by an assembly
//     attribute, no resource scan)
//   - deliberately NO culture satellite: NativeAOT cannot load one, so it would
//     silently degrade to the in-code default
//
// Both translations resolve from Localizer.Default with no services; the
// embed attribute lives in AssemblyInfo.cs, the files under Translations/ and
// Embedded/.
// ---------------------------------------------------------------------------
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

var fromFiles = Localizer.Default.Translate("fromFiles", "from files (default)");
var fromMainEmbed = Localizer.Default.Translate("fromMainEmbed", "from embed (default)");

Console.WriteLine("files:         " + fromFiles);
Console.WriteLine("main assembly: " + fromMainEmbed);

var ok = fromFiles == "Aus Dateien" && fromMainEmbed == "Aus dem Hauptassembly";
Console.WriteLine(ok ? "AOT OK" : "AOT BROKEN");
return ok ? 0 : 1;
