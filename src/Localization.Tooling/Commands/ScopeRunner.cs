using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// The one owner of the authoring commands' scan skeleton: under a status spinner, resolve the scope's built
/// assemblies, extract each one's source template, and hand every assembly that has translatable strings to the
/// command's own per-assembly action along with the catalog directory it writes to. Assemblies with no template
/// are skipped.
/// </summary>
internal static class ScopeRunner
{
    /// <summary>
    /// Runs <paramref name="perAssembly"/> for each in-scope assembly that has strings, passing its name, the
    /// catalog directory it belongs to, and its source template. The directory is always the
    /// <paramref name="outputFolder"/> subfolder (default <c>Translations</c>) of the assembly's own project — or,
    /// for a loose <c>--assembly</c>/<c>--input</c> path, beside the input base — never one shared flat folder.
    /// </summary>
    public static Task ForEachTemplateAsync(AuthoringScopeSettings settings, string? outputFolder, string verb, Func<string, string, Catalog, Task> perAssembly)
    {
        var folder = string.IsNullOrEmpty(outputFolder) ? "Translations" : outputFolder;
        ScopeOptions scope = settings.ToScope();
        return AnsiConsole.Status().StartAsync($"{verb}…", async ctx =>
        {
            using var extractor = new AssemblyStringExtractor();
            foreach (var path in ScopeResolver.Resolve(scope))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                ctx.Status($"{verb} {name}…");
                Catalog? template = TemplateBuilder.Build(extractor, path, settings.Source, settings.IncludeAnnotations);
                if (template is not null)
                {
                    await perAssembly(name, CatalogDirectoryResolver.CatalogDirectoryFor(path, scope, folder), template);
                }
            }
        });
    }
}
