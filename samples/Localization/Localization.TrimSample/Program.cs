using System.Globalization;
using ArchPillar.Extensions.Localization;

// ---------------------------------------------------------------------------
// Localization.TrimSample
//
// Validates the opt-in EMBED path under trimming / single-file / AOT:
//   - a main-assembly embedded catalog, discovered via an assembly attribute
//   - a culture satellite, discovered via an assembly attribute, resolved
//     lazily for de
//   - a self-check that prints OK/BROKEN: both German lines mean embed
//     discovery survived; the English defaults mean the trimmer dropped the
//     resources or the discovery reflection — and under NativeAOT the satellite
//     does not load and degrades to the in-code default (the point of the spike)
//
// The embed attributes live in AssemblyInfo.cs, the catalogs under Embedded/.
// ---------------------------------------------------------------------------
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

var fromMain = Localization.Default.Translate("fromMain", "from main (default)");
var fromSatellite = Localization.Default.Translate("fromSatellite", "from satellite (default)");

Console.WriteLine("main assembly: " + fromMain);
Console.WriteLine("satellite:     " + fromSatellite);

var ok = fromMain == "Aus dem Hauptassembly" && fromSatellite == "Aus dem Satelliten";
Console.WriteLine(ok ? "EMBED OK" : "EMBED BROKEN");
return ok ? 0 : 1;
