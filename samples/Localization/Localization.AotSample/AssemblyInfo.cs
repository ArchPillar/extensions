using ArchPillar.Extensions.Localization;

// Advertise the main-assembly embedded catalog (an attribute read, not a resource scan). No satellite marker:
// satellites do not load under NativeAOT, so this sample deliberately avoids them.
[assembly: LocalizationCatalog("aotmain.de.arb", "arb")]
