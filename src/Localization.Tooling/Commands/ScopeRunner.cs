using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// The one owner of the authoring commands' scan skeleton: under a status spinner, resolve the scope's built
/// assemblies, extract each one's source template, and hand every assembly that has translatable strings to the
/// command's own per-assembly action along with the catalog directory it writes to. Assemblies with no template
/// are skipped. It is also the one place the two destination options are reconciled, so every authoring command
/// resolves them identically.
/// </summary>
internal static class ScopeRunner
{
    /// <summary>
    /// Runs <paramref name="perAssembly"/> for each in-scope assembly that has strings, passing its name, the
    /// catalog directory it belongs to, and its source template. With <c>--output</c> that directory is the one
    /// given, shared by every assembly; otherwise it is the <c>--catalog-path</c> subfolder of the assembly's own
    /// project — or, for a loose <c>--assembly</c>/<c>--input</c> path, beside the input base.
    /// </summary>
    public static Task ForEachTemplateAsync(AuthoringScopeSettings settings, string verb, Func<string, string, Catalog, Task> perAssembly)
    {
        ScopeOptions scope = settings.ToScope();
        var flat = settings.FlatDirectory;
        return ToolConsole.StatusAsync($"{verb}…", async ctx =>
        {
            using var extractor = new AssemblyStringExtractor();
            foreach (var path in ScopeResolver.Resolve(scope))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                ToolConsole.Status(ctx, $"{verb} {name}…");
                Catalog? template = TemplateBuilder.Build(extractor, path, settings.Source, settings.IncludeAnnotations);
                if (template is not null)
                {
                    await perAssembly(name, flat ?? CatalogDirectoryResolver.CatalogDirectoryFor(path, scope, settings.CatalogFolder), template);
                }
            }
        });
    }
}
