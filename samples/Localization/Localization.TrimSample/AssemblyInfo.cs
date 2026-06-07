using ArchPillar.Extensions.Localization;

// Advertise the main-assembly embedded catalog (an attribute read, not a resource scan) and mark that this
// assembly also ships culture satellites, so the ambient store discovers both without an up-front scan.
[assembly: LocalizationCatalog("trimmain.de.arb", "arb")]
[assembly: LocalizationSatelliteCatalogs]
