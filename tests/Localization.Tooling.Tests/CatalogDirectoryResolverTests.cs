using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

public sealed class CatalogDirectoryResolverTests : IDisposable
{
    private readonly string _root;

    public CatalogDirectoryResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apldirs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ProjectCatalogDirectoryOf_AssemblyUnderProject_ReturnsTheProjectsCatalogFolder()
    {
        // A standard build layout: <project>/bin/<config>/<tfm>/<assembly>.dll beside <project>/App.Web.csproj.
        var projectDirectory = Path.Combine(_root, "App.Web");
        var binDirectory = Path.Combine(projectDirectory, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(binDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "App.Web.csproj"), "<Project />");
        var assembly = Path.Combine(binDirectory, "App.Web.dll");
        File.WriteAllText(assembly, string.Empty);

        var catalogDirectory = CatalogDirectoryResolver.ProjectCatalogDirectoryOf(assembly, "Translations");

        Assert.Equal(Path.Combine(projectDirectory, "Translations"), catalogDirectory);
    }

    [Fact]
    public void ProjectCatalogDirectoryOf_AssemblyOutsideAnyProject_ReturnsNull()
    {
        var loose = Path.Combine(_root, "loose", "App.Web.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
        File.WriteAllText(loose, string.Empty);

        Assert.Null(CatalogDirectoryResolver.ProjectCatalogDirectoryOf(loose, "Translations"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
