namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// The translation comments recovered from source, indexed by the string literal each was authored beside — a
/// call-site key or default, or an annotation's value. Built by <see cref="SourceCommentScanner"/> and consumed by
/// the template build to attach a <see cref="CatalogEntry.Comment"/> by identity (the entry's key or its source
/// default), never by source location — so a comment reaches its entry without the PDB, which cannot locate an
/// attribute or a field at all.
/// </summary>
internal sealed class CommentIndex
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _byLiteral;

    internal CommentIndex(IReadOnlyDictionary<string, IReadOnlyList<string>> byLiteral)
    {
        _byLiteral = byLiteral;
    }

    /// <summary>An index with no comments — the result when a scope has no readable source root.</summary>
    public static CommentIndex Empty { get; } = new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>
    /// The comment authored beside <paramref name="key"/> or its <paramref name="sourceMessage"/> default, or
    /// <see langword="null"/> when neither literal carried one. Checking both literals lets the author write the
    /// note by the key (a string id) or by the human-readable default, in the call or in either paired annotation.
    /// Comments found against both are combined in order and de-duplicated, one per line.
    /// </summary>
    public string? Lookup(string key, string sourceMessage)
    {
        var combined = new List<string>();
        Collect(key, combined);
        if (!string.Equals(key, sourceMessage, StringComparison.Ordinal))
        {
            Collect(sourceMessage, combined);
        }

        return combined.Count == 0 ? null : string.Join("\n", combined);
    }

    private void Collect(string literal, List<string> into)
    {
        if (!_byLiteral.TryGetValue(literal, out IReadOnlyList<string>? comments))
        {
            return;
        }

        foreach (var comment in comments)
        {
            if (!into.Contains(comment))
            {
                into.Add(comment);
            }
        }
    }
}
