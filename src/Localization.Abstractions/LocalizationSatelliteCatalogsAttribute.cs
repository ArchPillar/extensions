namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Marks an assembly that ships its translation catalogs as culture **satellite** assemblies (the opt-in
/// embed path: catalogs named <c>&lt;name&gt;.&lt;culture&gt;.&lt;ext&gt;</c> that MSBuild routes to
/// per-culture satellites). The ambient store probes only attributed assemblies, lazily per requested
/// culture, via <see cref="System.Reflection.Assembly.GetSatelliteAssembly(System.Globalization.CultureInfo)"/>
/// — so it never blindly probes (and catches exceptions from) assemblies that have no satellites.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class LocalizationSatelliteCatalogsAttribute : Attribute
{
}
