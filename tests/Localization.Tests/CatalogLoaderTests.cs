using System.Globalization;
using ArchPillar.Extensions.Localization.Catalogs;
using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The catalog loader's tolerance of work whose provider is not in its registry. The store builds work from its
/// <c>_providers</c> and loads it into its <c>_loader</c> through two separate lock-free reads, so a reconfigure that
/// lands between them hands a fresh loader an earlier configuration's provider. That stale work must be dropped, not
/// throw <see cref="KeyNotFoundException"/> — the new configuration reloads the in-use cultures against its own
/// providers. The read paths (<see cref="CatalogLoader.LoadedCatalogs"/>, <see cref="CatalogLoader.Forget"/>) already
/// tolerate an unknown provider; these tests pin the same tolerance on the open paths.
/// </summary>
public sealed class CatalogLoaderTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void Load_SynchronousWorkForUnregisteredProvider_IsDroppedWithoutThrowing()
    {
        var loader = new CatalogLoader([new StubProvider()]);
        var stale = new StubProvider();
        var opened = false;
        CatalogDescriptor descriptor = SyncDescriptor("de", () =>
        {
            opened = true;
            return CatalogFor("de");
        });

        var raised = 0;
        IReadOnlyList<Task> tasks = loader.Load([(stale, descriptor)], () => raised++);

        // The unregistered provider short-circuits before its source is opened, so nothing is registered or published.
        Assert.Empty(tasks);
        Assert.False(opened);
        Assert.Equal(0, raised);
        Assert.Empty(loader.LoadedCatalogs([stale]));
    }

    [Fact]
    public async Task Load_AsynchronousWorkForUnregisteredProvider_IsDroppedWithoutFaultingAsync()
    {
        var loader = new CatalogLoader([new StubProvider()]);
        var stale = new StubProvider();
        CatalogDescriptor descriptor = AsyncDescriptor("de", () => CatalogFor("de"));

        var raised = 0;
        IReadOnlyList<Task> tasks = loader.Load([(stale, descriptor)], () => raised++);

        // The background fetch runs, but the missing registry entry drops the fetched catalog instead of faulting the
        // task with KeyNotFoundException — draining it must not throw.
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, raised);
        Assert.Empty(loader.LoadedCatalogs([stale]));
    }

    [Fact]
    public void Load_SynchronousWorkForRegisteredProvider_StillLoads()
    {
        var provider = new StubProvider();
        var loader = new CatalogLoader([provider]);

        var raised = 0;
        IReadOnlyList<Task> tasks = loader.Load([(provider, SyncDescriptor("de", () => CatalogFor("de")))], () => raised++);

        // A known provider is unaffected by the guard: the catalog registers and publishes as before.
        Assert.Empty(tasks);
        Assert.Equal(1, raised);
        Catalog loaded = Assert.Single(loader.LoadedCatalogs([provider]));
        Assert.Equal(_german.Name, loaded.Culture);
    }

    private static Catalog CatalogFor(string culture) => new()
    {
        Culture = culture,
        Entries = []
    };

    private static CatalogDescriptor SyncDescriptor(string culture, Func<Catalog> open) => new()
    {
        Culture = culture,
        Format = "arb",
        Name = culture + ".arb",
        Source = new CatalogSource.Synchronous(open)
    };

    private static CatalogDescriptor AsyncDescriptor(string culture, Func<Catalog> open) => new()
    {
        Culture = culture,
        Format = "arb",
        Name = culture + ".arb",
        Source = new CatalogSource.Asynchronous(async _ =>
        {
            await Task.Yield();
            return open();
        })
    };

    // A minimal provider identity: the loader keys its registry by provider reference and never calls back into the
    // provider during a load, so an empty inventory is all these tests need.
    private sealed class StubProvider : ICatalogProvider
    {
        public IReadOnlyList<CatalogDescriptor> Catalogs { get; } = [];

        public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture) => [];

        public IDisposable Watch(Action<CatalogDescriptor> onChanged) => NoOpWatch.Instance;

        private sealed class NoOpWatch : IDisposable
        {
            public static readonly NoOpWatch Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
