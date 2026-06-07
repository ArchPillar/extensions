using System.Globalization;
using ArchPillar.Extensions.Localization;

// The recommended pattern for a NativeAOT app: localize through the two paths that survive AOT — a loose
// file beside the binary and a catalog embedded in the main assembly. Both resolve from the ambient store
// with no services. (A culture satellite would NOT load under AOT and would fall back to the in-code
// default; this sample avoids it — see Localization.TrimSample for that validation.)
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

var fromFiles = Localization.Default.Translate("fromFiles", "from files (default)");
var fromMainEmbed = Localization.Default.Translate("fromMainEmbed", "from embed (default)");

Console.WriteLine("files:         " + fromFiles);
Console.WriteLine("main assembly: " + fromMainEmbed);

var ok = fromFiles == "Aus Dateien" && fromMainEmbed == "Aus dem Hauptassembly";
Console.WriteLine(ok ? "AOT OK" : "AOT BROKEN");
return ok ? 0 : 1;
