using System.Globalization;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The directory catalog provider: it scans the directory at construction and lists the recognized translation
/// files as descriptors (culture from the file name, format from the extension) whose source is
/// <see cref="CatalogSource.Synchronous"/>. Listing never reads bytes — the opener does — and both list and probe
/// complete synchronously. <see cref="ICatalogProvider.CatalogsFor"/> filters to a culture; <see cref="ICatalogProvider.Watch"/>
/// reports the changed descriptor.
/// </summary>
public sealed class DirectoryCatalogProviderTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void Catalogs_ReturnsDescriptorsForRecognizedFilesOnly()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");
            WriteArb(directory, "fr", "Bonjour");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "ignored");

            var provider = new DirectoryCatalogProvider(directory);
            IReadOnlyList<CatalogDescriptor> descriptors = provider.Catalogs;

            Assert.Equal(2, descriptors.Count);
            Assert.Contains(descriptors, descriptor => descriptor.Culture == "de" && descriptor.Name == "App.de.arb" && descriptor.Format == ".arb");
            Assert.Contains(descriptors, descriptor => descriptor.Culture == "fr");
            Assert.DoesNotContain(descriptors, descriptor => descriptor.Name == "notes.txt");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Catalogs_DescriptorsCarryASynchronousLoad()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");

            var provider = new DirectoryCatalogProvider(directory);
            CatalogDescriptor descriptor = Assert.Single(provider.Catalogs);

            Assert.IsType<CatalogSource.Synchronous>(descriptor.Source);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Catalogs_MissingDirectoryReturnsEmpty()
    {
        var provider = new DirectoryCatalogProvider(Path.Combine(Path.GetTempPath(), "apl-does-not-exist-" + Guid.NewGuid().ToString("N")));

        Assert.Empty(provider.Catalogs);
    }

    [Fact]
    public void SynchronousSource_OpensTheCatalogBytes()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");

            var provider = new DirectoryCatalogProvider(directory);
            CatalogDescriptor descriptor = Assert.Single(provider.Catalogs);
            CatalogSource.Synchronous sync = Assert.IsType<CatalogSource.Synchronous>(descriptor.Source);

            using Stream stream = sync.Open();
            Catalog catalog = new ArbTranslationFormat().Read(stream);

            Assert.Equal("de", catalog.Culture);
            Assert.Contains(catalog.Entries, entry => entry.TranslatedMessage == "Hallo");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogsFor_ReturnsOnlyTheRequestedCulture()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");
            WriteArb(directory, "fr", "Bonjour");

            var provider = new DirectoryCatalogProvider(directory);
            IReadOnlyList<CatalogDescriptor> german = provider.CatalogsFor(_german);

            CatalogDescriptor descriptor = Assert.Single(german);
            Assert.Equal("de", descriptor.Culture);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogsFor_UnknownCultureReturnsEmpty()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");

            var provider = new DirectoryCatalogProvider(directory);
            IReadOnlyList<CatalogDescriptor> result = provider.CatalogsFor(_french);

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Watch_MissingDirectoryReturnsNoOpHandle()
    {
        var provider = new DirectoryCatalogProvider(Path.Combine(Path.GetTempPath(), "apl-missing-" + Guid.NewGuid().ToString("N")));

        using IDisposable handle = provider.Watch(_ => { });

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task Watch_FiresWithTheChangedDescriptorAndStopsOnDisposeAsync()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");
            var provider = new DirectoryCatalogProvider(directory, TimeSpan.FromMilliseconds(20));
            using var fired = new SemaphoreSlim(0);
            CatalogDescriptor? changed = null;

            IDisposable handle = provider.Watch(descriptor =>
            {
                changed = descriptor;
                fired.Release();
            });
            WriteArb(directory, "fr", "Bonjour");

            Assert.True(await fired.WaitAsync(TimeSpan.FromSeconds(5)), "watch did not fire on change");
            Assert.NotNull(changed);
            Assert.Equal("fr", changed!.Culture);
            handle.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apl-dirprovider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteArb(string directory, string culture, string message) =>
        File.WriteAllText(Path.Combine(directory, $"App.{culture}.arb"), $$"""
            {
              "@@locale": "{{culture}}",
              "@@x-category": "Greeting",
              "hello": "{{message}}",
              "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
            }
            """, Encoding.UTF8);
}
