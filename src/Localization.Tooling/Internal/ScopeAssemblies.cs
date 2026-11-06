namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// What one scope resolution answers: the assemblies to scan, and the directories their projects compile
/// against. They travel together because the second depends on the first: the directories are those of the
/// configuration each assembly was built in.
/// </summary>
/// <param name="Assemblies">The in-scope assembly paths, deduplicated and ordered.</param>
/// <param name="ReferenceDirectories">Where the assemblies those projects reference actually live; empty for a
/// scope that names built assemblies directly and so has no project to ask.</param>
internal sealed record ScopeAssemblies(IReadOnlyList<string> Assemblies, IReadOnlyList<string> ReferenceDirectories);
