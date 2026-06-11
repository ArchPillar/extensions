using System.Xml.Linq;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>How a command was told which assemblies to operate on.</summary>
internal sealed record ScopeOptions(string? Assembly, string? Input, string? Project, string? Solution, bool Recurse);

/// <summary>
/// Resolves a project / solution / directory scope to the set of built assemblies that actually carry a baked
/// localization template — so the authoring commands work over a whole app at once instead of one assembly at
/// a time. A single explicit <c>--assembly</c> is honoured for the low-level path; everything else fans out
/// over a build output tree and keeps only assemblies with strings.
/// </summary>
internal static class ScopeResolver
{
    /// <summary>Returns the in-scope templates, deduplicated by assembly name (newest build wins).</summary>
    public static IReadOnlyList<BakedTemplate> Resolve(ScopeOptions scope)
    {
        var byName = new Dictionary<string, (BakedTemplate Template, DateTime Written)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in CandidateAssemblies(scope))
        {
            BakedTemplate? template = BakedTemplateReader.TryRead(path);
            if (template is null)
            {
                continue;
            }

            DateTime written = File.GetLastWriteTimeUtc(path);
            // Multi-targeting puts the same assembly under several TFM folders; keep one, preferring the most
            // recently built so a fresh extract never reads a stale duplicate.
            if (!byName.TryGetValue(template.AssemblyName, out (BakedTemplate Template, DateTime Written) existing) || written > existing.Written)
            {
                byName[template.AssemblyName] = (template, written);
            }
        }

        return [.. byName.Values.Select(entry => entry.Template).OrderBy(t => t.AssemblyName, StringComparer.Ordinal)];
    }

    private static IEnumerable<string> CandidateAssemblies(ScopeOptions scope)
    {
        if (scope.Assembly is { Length: > 0 } assembly)
        {
            return [Path.GetFullPath(assembly)];
        }

        IEnumerable<string> roots = scope switch
        {
            { Input: { Length: > 0 } input } => [input],
            { Project: { Length: > 0 } project } => ProjectBinDirectories(Path.GetFullPath(project), scope.Recurse),
            { Solution: { Length: > 0 } solution } => SolutionProjects(Path.GetFullPath(solution))
                .SelectMany(p => ProjectBinDirectories(p, recurse: false)),
            _ => throw new ArgumentException("Specify a scope: --assembly, --input <dir>, --project <csproj> [--recurse], or --solution <sln>.")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // The project's own bin tree, plus — when recursing — the bin trees of every project it references
    // transitively, so `--project App --recurse` covers the libraries the app pulls in.
    private static IEnumerable<string> ProjectBinDirectories(string projectPath, bool recurse)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(projectPath);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current) || !File.Exists(current))
            {
                continue;
            }

            yield return Path.Combine(Path.GetDirectoryName(current)!, "bin");
            if (!recurse)
            {
                continue;
            }

            foreach (var referenced in ProjectReferences(current))
            {
                queue.Enqueue(referenced);
            }
        }
    }

    private static IEnumerable<string> ProjectReferences(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        foreach (XElement element in XDocument.Load(projectPath).Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value;
            if (!string.IsNullOrEmpty(include))
            {
                yield return Path.GetFullPath(Path.Combine(directory, include!.Replace('\\', Path.DirectorySeparatorChar)));
            }
        }
    }

    private static IEnumerable<string> SolutionProjects(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        if (Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return XDocument.Load(solutionPath)
                .Descendants()
                .Where(e => e.Name.LocalName == "Project")
                .Select(e => e.Attribute("Path")?.Value)
                .Where(path => path is { Length: > 0 } && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFullPath(Path.Combine(directory, path!.Replace('\\', Path.DirectorySeparatorChar))));
        }

        // Classic .sln: lines like  Project("{GUID}") = "Name", "relative\path.csproj", "{GUID}"
        return File.ReadLines(solutionPath)
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(ProjectPathFromSlnLine)
            .Where(path => path is { Length: > 0 })
            .Select(path => Path.GetFullPath(Path.Combine(directory, path!.Replace('\\', Path.DirectorySeparatorChar))))!;
    }

    private static string? ProjectPathFromSlnLine(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2)
        {
            return null;
        }

        var quoted = parts[1].Trim().Trim('"');
        return quoted.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? quoted : null;
    }
}
