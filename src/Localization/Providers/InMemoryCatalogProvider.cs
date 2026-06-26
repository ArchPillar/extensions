using System.Globalization;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// An <see cref="ICatalogProvider"/> over catalogs already parsed in memory — for handing the store a fixed set
/// of <see cref="Catalog"/>s with no file system or network. Each catalog is listed as its own descriptor and
/// produced as-is (no bytes, no parsing), so the store loads it straight onto its synchronous path. Catalogs never
/// change after construction, so <see cref="Watch"/> is a no-op. Register it through
/// <see cref="LocalizerOptions.Providers"/> to layer host overrides or seed a test without touching disk.
/// </summary>
public sealed class InMemoryCatalogProvider : ICatalogProvider
{
    /// <summary>
    /// Initializes a new <see cref="InMemoryCatalogProvider"/> over <paramref name="catalogs"/>, listing one
    /// descriptor per catalog in order — a later catalog wins on per-culture overlap, as everywhere else.
    /// </summary>
    /// <param name="catalogs">The parsed catalogs to serve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalogs"/> is <see langword="null"/>.</exception>
    public InMemoryCatalogProvider(IEnumerable<Catalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        Catalogs = [
          ..catalogs.Select((catalog, index) => new CatalogDescriptor
          {
              Culture = catalog.Culture,
              Format = "memory",
              Name = catalog.Culture + "#" + index.ToString(CultureInfo.InvariantCulture),
              Source = new CatalogSource.Synchronous(() => catalog)
          })
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return [.. Catalogs.Where(descriptor => string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))];
    }

    /// <inheritdoc />
    public IDisposable Watch(Action<CatalogDescriptor> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        return NoOpWatch.Instance;
    }
}
