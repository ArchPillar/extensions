using System.Globalization;
using ArchPillar.Extensions.Localization;

// Validates the opt-in EMBED path end to end: a main-assembly embedded catalog and a culture satellite,
// both discovered by the ambient store via assembly attributes (no scan) and resolved lazily for de. Publish
// this trimmed / single-file / AOT and run it: if both German lines print, embed discovery survived; if the
// English defaults print instead, the trimmer dropped the resources or the discovery reflection.
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

var fromMain = Localization.Default.Translate("fromMain", "from main (default)");
var fromSatellite = Localization.Default.Translate("fromSatellite", "from satellite (default)");

Console.WriteLine("main assembly: " + fromMain);
Console.WriteLine("satellite:     " + fromSatellite);

var ok = fromMain == "Aus dem Hauptassembly" && fromSatellite == "Aus dem Satelliten";
Console.WriteLine(ok ? "EMBED OK" : "EMBED BROKEN");
return ok ? 0 : 1;
