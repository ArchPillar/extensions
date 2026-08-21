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
    /// <para>
    /// Returns how many assemblies were scanned, which is what tells a caller's empty result apart from this
    /// method's: nothing <em>found</em> is an answer, nothing <em>scanned</em> is a failure.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The scope resolved to no built assembly at all.</exception>
    public static Task<int> ForEachTemplateAsync(AuthoringScopeSettings settings, string verb, Func<string, string, Catalog, Task> perAssembly)
    {
        ScopeOptions scope = settings.ToScope();
        var flat = settings.FlatDirectory;
        return ToolConsole.StatusAsync($"{verb}…", async ctx =>
        {
            IReadOnlyList<string> assemblies = ScopeResolver.Resolve(scope);

            // Nothing to scan is a failed command, not a quiet success. The projects evaluated (or this would
            // already have thrown), so the scope is real and simply has no build output in it — an unbuilt or
            // cleaned tree, or a path pointed where output never lands. Returning 0 there tells a CI gate that a
            // check passed when nothing was ever looked at, which is the one answer a gate must never give.
            if (assemblies.Count == 0)
            {
                throw new ArgumentException(
                    "No built assemblies found in the given scope, so nothing was scanned. Build the projects "
                    + "first, or point --input <dir> or --assembly <dll> at build output that exists.");
            }

            using var extractor = new AssemblyStringExtractor();
            // Translation comments cannot be read from the binary, so scan the project's source once per root and
            // reuse it for every assembly it built (multi-targeting produces several). The same root is what file
            // references are recorded relative to, so the catalog carries no machine-specific path.
            var commentsByRoot = new Dictionary<string, CommentIndex>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in assemblies)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                ToolConsole.Status(ctx, $"{verb} {name}…");
                var root = CatalogDirectoryResolver.ProjectRootOf(path);
                // References are opt-in (--references): with no root to record them against, none are recorded.
                Catalog? template = TemplateBuilder.Build(
                    extractor,
                    path,
                    settings.Source,
                    CommentsFor(root, commentsByRoot),
                    settings.IncludeReferences ? root : null,
                    settings.IncludeAnnotations);
                if (template is not null)
                {
                    await perAssembly(name, flat ?? CatalogDirectoryResolver.CatalogDirectoryFor(path, scope, settings.CatalogFolder), template);
                }
            }

            return assemblies.Count;
        });
    }

    // The comments scanned from the assembly's project source, one scan per project root shared across its
    // assemblies. An assembly with no project tree (a loose --assembly/--input path) has no source root to scan.
    private static CommentIndex CommentsFor(string? root, Dictionary<string, CommentIndex> cache)
    {
        if (root is null)
        {
            return CommentIndex.Empty;
        }

        if (!cache.TryGetValue(root, out CommentIndex? index))
        {
            index = SourceCommentScanner.Scan(root);
            cache[root] = index;
        }

        return index;
    }
}
