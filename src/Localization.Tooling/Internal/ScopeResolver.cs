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
    /// <summary>Returns the in-scope assembly paths, deduplicated by file name (newest build wins). Whether an
    /// assembly actually has translatable strings is decided later, when its IL is read.</summary>
    public static IReadOnlyList<string> Resolve(ScopeOptions scope)
    {
        var byName = new Dictionary<string, (string Path, DateTime Written)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in CandidateAssemblies(scope))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            DateTime written = File.GetLastWriteTimeUtc(path);
            // Multi-targeting puts the same assembly under several TFM folders; keep one, preferring the most
            // recently built so a fresh extract never reads a stale duplicate.
            if (!byName.TryGetValue(name, out (string Path, DateTime Written) existing) || written > existing.Written)
            {
                byName[name] = (path, written);
            }
        }

        return [.. byName.Values.Select(entry => entry.Path).OrderBy(Path.GetFileNameWithoutExtension, StringComparer.Ordinal)];
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
            { Project: { } project } => ProjectBinDirectories(ResolveSingleFile(project, "project", "*.csproj"), scope.Recurse),
            { Solution: { } solution } => SolutionProjects(ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"))
                .SelectMany(p => ProjectBinDirectories(p, recurse: false)),
            _ => DiscoverInCurrentDirectory(scope.Recurse)
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // With no scope at all, default to the current directory like `dotnet build`: a lone solution wins, else
    // a lone project; an ambiguous or empty directory is an error rather than a guess.
    private static IEnumerable<string> DiscoverInCurrentDirectory(bool recurse)
    {
        var directory = Directory.GetCurrentDirectory();
        List<string> solutions = [.. Directory.EnumerateFiles(directory, "*.sln").Concat(Directory.EnumerateFiles(directory, "*.slnx"))];
        if (solutions.Count > 1)
        {
            throw new ArgumentException("Multiple solution files in the current directory; pass one with --solution <path>.");
        }

        if (solutions.Count == 1)
        {
            return SolutionProjects(solutions[0]).SelectMany(p => ProjectBinDirectories(p, recurse: false));
        }

        List<string> projects = [.. Directory.EnumerateFiles(directory, "*.csproj")];
        return projects.Count switch
        {
            1 => ProjectBinDirectories(projects[0], recurse),
            0 => throw new ArgumentException("No project or solution found in the current directory. Run from your app folder, or pass --project, --solution, or --input <dir>."),
            _ => throw new ArgumentException("Multiple project files in the current directory; pass one with --project <path>.")
        };
    }

    // Resolves a --project / --solution value to a single file. A file path is used as-is; an empty value
    // means the current directory, a directory value means that directory — in which case the lone matching
    // file is taken (matching how `dotnet build` finds the project/solution when run in a folder), and an
    // ambiguous or empty directory is an error rather than a silent guess.
    private static string ResolveSingleFile(string value, string kind, params string[] patterns)
    {
        if (value.Length > 0 && File.Exists(value))
        {
            return Path.GetFullPath(value);
        }

        var directory = value.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(value);
        if (!Directory.Exists(directory))
        {
            throw new ArgumentException($"'{value}' is not a {kind} file or a directory.");
        }

        List<string> matches = [.. patterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern)).Distinct(StringComparer.OrdinalIgnoreCase)];
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"No {kind} file found in '{directory}'."),
            _ => throw new ArgumentException($"Multiple {kind} files in '{directory}'; pass one explicitly with --{kind} <path>.")
        };
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
