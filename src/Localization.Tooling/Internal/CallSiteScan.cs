namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// What one assembly's IL scan found, and what it could not answer. <paramref name="UnresolvedAssemblies"/> is
/// the honest half: a call that binds a literal string into an assembly the resolver could not open carries
/// attributes nobody read, so whether it was a translation site is unknown. A scan with any of these is
/// <em>incomplete</em>, and a caller must not treat a key's absence from it as proof the key is obsolete.
/// </summary>
/// <param name="Sites">The recognised call sites.</param>
/// <param name="UnresolvedAssemblies">The assemblies that could not be resolved, ordered, without duplicates.</param>
/// <param name="UnclassifiedCallCount">How many call sites went unclassified because of them.</param>
internal sealed record CallSiteScan(
    IReadOnlyList<RawCallSite> Sites,
    IReadOnlyList<string> UnresolvedAssemblies,
    int UnclassifiedCallCount)
{
    /// <summary>A scan that found nothing and left nothing unanswered.</summary>
    public static readonly CallSiteScan Empty = new([], [], 0);

    /// <summary>Whether every call site in the assembly could be classified.</summary>
    public bool IsComplete => UnresolvedAssemblies.Count == 0;
}

/// <summary>
/// Accumulates the calls a scan could not classify: how many, and which assemblies were missing. Mutable and
/// scan-local by design — it is filled during one assembly's walk and frozen into a <see cref="CallSiteScan"/>.
/// </summary>
internal sealed class UnclassifiedCalls
{
    private readonly HashSet<string> _assemblies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many call sites went unclassified.</summary>
    public int Count { get; private set; }

    /// <summary>The distinct assemblies that could not be resolved.</summary>
    public IReadOnlyCollection<string> Assemblies => _assemblies;

    /// <summary>Records one call site that could not be classified because <paramref name="assembly"/> was missing.</summary>
    public void Record(string assembly)
    {
        _assemblies.Add(assembly);
        Count++;
    }
}
