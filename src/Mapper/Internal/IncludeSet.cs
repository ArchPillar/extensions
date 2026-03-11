namespace ArchPillar.Mapper.Internal;

/// <summary>
/// A recursive tree of optional-property include requests.
/// <see cref="Names"/> holds the property names to include at this level;
/// <see cref="Nested"/> maps property names to the <see cref="IncludeSet"/>
/// for the nested mapper's destination type.
/// </summary>
internal sealed class IncludeSet
{
    public static readonly IncludeSet Empty = new();
    public static readonly IncludeSet All   = new() { IncludeAll = true };

    public bool                           IncludeAll { get; init; }
    public HashSet<string>                Names      { get; } = [];
    public Dictionary<string, IncludeSet> Nested     { get; } = [];

    // -------------------------------------------------------------------------
    // Parse methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an <see cref="IncludeSet"/> from a list of <see cref="IncludeEntry"/>
    /// objects produced by <see cref="ProjectionOptions{TDest}"/>.
    /// Multiple entries for the same property are merged idempotently.
    /// </summary>
    public static IncludeSet Parse(IReadOnlyList<IncludeEntry> entries)
    {
        var result = new IncludeSet();
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case ScalarInclude s:
                    result.Names.Add(s.MemberName);
                    break;

                case NestedInclude c:
                    result.Names.Add(c.MemberName);
                    var child = Parse(c.NestedEntries);
                    if (result.Nested.TryGetValue(c.MemberName, out var existing))
                        existing.MergeFrom(child);
                    else
                        result.Nested[c.MemberName] = child;
                    break;

                case StringPathInclude { Path: var p }:
                    AddStringPath(result, p);
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Builds an <see cref="IncludeSet"/> from a list of dot-separated string
    /// paths (e.g. <c>"CustomerName"</c>, <c>"Lines.SupplierName"</c>).
    /// Each segment becomes a name at the corresponding depth; intermediate
    /// segments create nested entries automatically.
    /// </summary>
    public static IncludeSet Parse(IReadOnlyList<string> paths)
    {
        var result = new IncludeSet();
        foreach (var path in paths)
            AddStringPath(result, path);
        return result;
    }

    /// <summary>
    /// Merges all names and nested entries from <paramref name="other"/> into
    /// this instance. Nested entries for the same property are merged recursively.
    /// </summary>
    public void MergeFrom(IncludeSet other)
    {
        foreach (var name in other.Names)
            Names.Add(name);

        foreach (var (name, nested) in other.Nested)
        {
            if (Nested.TryGetValue(name, out var existing))
                existing.MergeFrom(nested);
            else
                Nested[name] = nested;
        }
    }

    private static void AddStringPath(IncludeSet set, string path)
    {
        var segments = path.Split('.');
        var current = set;
        for (var i = 0; i < segments.Length; i++)
        {
            current.Names.Add(segments[i]);
            if (i < segments.Length - 1)
            {
                if (!current.Nested.TryGetValue(segments[i], out var child))
                {
                    child = new IncludeSet();
                    current.Nested[segments[i]] = child;
                }
                current = child;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Entry types — used by MapOptions / ProjectionOptions to accumulate
    // includes before parsing into an IncludeSet tree.
    // -------------------------------------------------------------------------

    internal abstract class IncludeEntry;

    internal sealed class ScalarInclude(string memberName) : IncludeEntry
    {
        public string MemberName { get; } = memberName;
    }

    internal sealed class StringPathInclude(string path) : IncludeEntry
    {
        public string Path { get; } = path;
    }

    /// <summary>
    /// A property include carrying the nested include entries for a child
    /// mapper's element type. The entries are resolved eagerly (no factory)
    /// and merged idempotently when the same property is included multiple times.
    /// </summary>
    internal sealed class NestedInclude(string memberName, IReadOnlyList<IncludeEntry> nestedEntries)
        : IncludeEntry
    {
        public string                       MemberName    { get; } = memberName;
        public IReadOnlyList<IncludeEntry>  NestedEntries { get; } = nestedEntries;
    }
}
