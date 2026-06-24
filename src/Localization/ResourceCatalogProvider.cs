using System.Globalization;
using System.Reflection;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// An <see cref="ICatalogProvider"/> over the catalogs assemblies ship: those embedded as manifest resources
/// and declared by <see cref="LocalizationCatalogAttribute"/>, and those routed to culture
/// <see cref="LocalizationSatelliteCatalogsAttribute">satellite</see> assemblies. At construction it scans the
/// currently-loaded assemblies for their embedded catalogs (these are enumerable) into <see cref="Catalogs"/>;
/// <see cref="CatalogsFor(CultureInfo)"/> additionally probes each satellite-marked assembly for the requested
/// culture (satellites cannot be enumerated, only probed). <see cref="Watch"/> reports catalogs that appear when
/// further assemblies load. All reflection is guarded, so a dynamic or partly-loaded assembly never takes the
/// host down. Listing and opening complete synchronously, so the store can satisfy a live culture switch from
/// its synchronous lookup path.
/// </summary>
public sealed class ResourceCatalogProvider : ICatalogProvider
{
    private readonly TranslationFormatRegistry _registry = BuiltInTranslationFormats.CreateRegistry();

    /// <summary>Initializes a new <see cref="ResourceCatalogProvider"/>, scanning the loaded assemblies now.</summary>
    public ResourceCatalogProvider()
    {
        var descriptors = new List<CatalogDescriptor>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            descriptors.AddRange(EmbeddedDescriptors(assembly));
        }

        Catalogs = descriptors;
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        var descriptors = new List<CatalogDescriptor>();
        foreach (CatalogDescriptor descriptor in Catalogs)
        {
            if (string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))
            {
                descriptors.Add(descriptor);
            }
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (HasSatelliteMarker(assembly))
            {
                descriptors.AddRange(SatelliteDescriptors(assembly, culture));
            }
        }

        return descriptors;
    }

    /// <inheritdoc />
    public IDisposable Watch(Action<CatalogDescriptor> onChanged)
    {
        if (onChanged is null)
        {
            throw new ArgumentNullException(nameof(onChanged));
        }

        return new AssemblyLoadWatch(this, onChanged);
    }

    // The embedded [LocalizationCatalog] catalogs declared on one assembly, as descriptors that open the named
    // manifest resource synchronously. Attribute access is guarded: a dynamic or reflection-only assembly may
    // refuse it, in which case the assembly contributes nothing.
    private List<CatalogDescriptor> EmbeddedDescriptors(Assembly assembly)
    {
        var descriptors = new List<CatalogDescriptor>();
        foreach (LocalizationCatalogAttribute attribute in SafeAttributes(assembly))
        {
            if (_registry.ResolveById(attribute.Format) is null)
            {
                continue;
            }

            var resourceName = attribute.ResourceName;
            Assembly owner = assembly;
            descriptors.Add(new CatalogDescriptor
            {
                Culture = CultureFromName(resourceName),
                Format = attribute.Format,
                Name = resourceName,
                Source = new CatalogSource.Synchronous(() => OpenResource(owner, resourceName))
            });
        }

        return descriptors;
    }

    // The catalogs in one assembly's satellite for a culture, as descriptors. GetSatelliteAssembly and the
    // resource-name read take the CLR loader lock and may throw when the culture has no satellite; both are
    // guarded so a missing satellite is simply "this assembly doesn't ship this language".
    private List<CatalogDescriptor> SatelliteDescriptors(Assembly assembly, CultureInfo culture)
    {
        // GetSatelliteAssembly throws FileNotFoundException/CultureNotFoundException when the culture has no
        // satellite; any other reflection failure is likewise "this assembly ships nothing here".
        Assembly? satellite;
        try
        {
            satellite = assembly.GetSatelliteAssembly(culture);
        }
        catch (Exception)
        {
            return [];
        }

        string[] names;
        try
        {
            names = satellite.GetManifestResourceNames();
        }
        catch (Exception)
        {
            return [];
        }

        var descriptors = new List<CatalogDescriptor>();
        foreach (var name in names)
        {
            if (_registry.ResolveByExtension(Path.GetExtension(name)) is null)
            {
                continue;
            }

            var resourceName = name;
            Assembly owner = satellite;
            descriptors.Add(new CatalogDescriptor
            {
                Culture = culture.Name,
                Format = Path.GetExtension(name),
                Name = resourceName,
                Source = new CatalogSource.Synchronous(() => OpenResource(owner, resourceName))
            });
        }

        return descriptors;
    }

    private static Stream OpenResource(Assembly assembly, string resourceName)
    {
        Stream? stream = assembly.GetManifestResourceStream(resourceName);
        return stream ?? throw new FileNotFoundException($"The embedded catalog resource '{resourceName}' was not found.", resourceName);
    }

    private static bool HasSatelliteMarker(Assembly assembly)
    {
        try
        {
            return assembly.GetCustomAttribute<LocalizationSatelliteCatalogsAttribute>() is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<LocalizationCatalogAttribute> SafeAttributes(Assembly assembly)
    {
        try
        {
            return assembly.GetCustomAttributes<LocalizationCatalogAttribute>();
        }
        catch (Exception)
        {
            return [];
        }
    }

    // The culture tag a resource name ends with: App.de.arb -> "de", de.arb -> "de". The same rule the
    // directory provider uses, keyed off the {name}.{culture}.{ext} naming convention.
    private static string CultureFromName(string resourceName)
    {
        var name = Path.GetFileNameWithoutExtension(resourceName);
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    // A watch over assembly loads: a newly-loaded assembly may carry embedded catalogs (a plugin) or be a
    // satellite, so a load is a "catalogs may have appeared" signal. The handler builds the new assembly's
    // embedded descriptors and reports each through the callback. It runs while the CLR loader lock is held, so
    // it does only the guarded reflection the construction scan does and never calls back into a locked store
    // path beyond the callback itself. Disposing unsubscribes.
    private sealed class AssemblyLoadWatch : IDisposable
    {
        private readonly ResourceCatalogProvider _provider;
        private readonly Action<CatalogDescriptor> _onChanged;
        private readonly AssemblyLoadEventHandler _handler;

        public AssemblyLoadWatch(ResourceCatalogProvider provider, Action<CatalogDescriptor> onChanged)
        {
            _provider = provider;
            _onChanged = onChanged;
            _handler = OnAssemblyLoad;
            AppDomain.CurrentDomain.AssemblyLoad += _handler;
        }

        public void Dispose() => AppDomain.CurrentDomain.AssemblyLoad -= _handler;

        private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
        {
            foreach (CatalogDescriptor descriptor in _provider.EmbeddedDescriptors(args.LoadedAssembly))
            {
                _onChanged(descriptor);
            }
        }
    }
}
