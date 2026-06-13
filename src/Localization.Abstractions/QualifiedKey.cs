namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The single convention for the on-disk identity of a catalog entry: the translation category qualifies
/// the key so an entry is unique across categories (and contexts) in one file. This is an ARB concern — its
/// flat JSON object holds one member per entry, so the category has to live in the member name; the
/// structured formats (XLIFF, PO) keep the bare key and carry the category in a separate note. A global
/// (uncategorized) entry is therefore written as its <em>bare</em> key — matching standard ARB and what
/// translation tools expect — with the sole exception of a key that begins with <c>@</c>, which is escaped
/// with a leading <c>::</c> so it is never confused with ARB's <c>@</c>-metadata members.
/// </summary>
public static class QualifiedKey
{
    private const string CategorySeparator = "::";
    private const string MetadataPrefix = "@";

    /// <summary>
    /// Qualifies <paramref name="key"/> with its <paramref name="category"/> (and <paramref name="context"/>
    /// when present) into the on-disk identity — for example <c>Acme.Labels::save</c>, <c>greeting</c>
    /// (global), or <c>Acme.Menu::post (#verb)</c>. A global key that begins with <c>@</c> is escaped as
    /// <c>::@key</c>.
    /// </summary>
    /// <param name="category">The translation category (empty for the global namespace).</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <returns>The qualified on-disk identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static string Qualify(string category, string key, string? context)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        string member;
        if (string.IsNullOrEmpty(category))
        {
            // Global namespace: the bare key is the member, except a key beginning with "@" is escaped with
            // the separator so it is never read back as an ARB "@"-metadata member.
            member = key.StartsWith(MetadataPrefix, StringComparison.Ordinal) ? CategorySeparator + key : key;
        }
        else
        {
            member = category + CategorySeparator + key;
        }

        return string.IsNullOrEmpty(context) ? member : member + " (#" + context + ")";
    }

    /// <summary>
    /// Recovers the bare key from a qualified identity, given the entry's known <paramref name="category"/>
    /// and <paramref name="context"/> (read from the entry's own metadata). The known prefix and suffix are
    /// stripped exactly once, so the key is recovered regardless of its content.
    /// </summary>
    /// <param name="qualified">The qualified on-disk identity.</param>
    /// <param name="category">The entry's category.</param>
    /// <param name="context">The entry's optional context.</param>
    /// <returns>The bare key.</returns>
    public static string Unqualify(string qualified, string category, string? context)
    {
        var key = qualified ?? string.Empty;

        var prefix = (category ?? string.Empty) + CategorySeparator;
        if (key.StartsWith(prefix, StringComparison.Ordinal))
        {
#if NETSTANDARD2_0
            key = key.Substring(prefix.Length);
#else
            key = key[prefix.Length..];
#endif
        }

        if (!string.IsNullOrEmpty(context))
        {
            var suffix = " (#" + context + ")";
            if (key.Length >= suffix.Length && key.EndsWith(suffix, StringComparison.Ordinal))
            {
#if NETSTANDARD2_0
                key = key.Substring(0, key.Length - suffix.Length);
#else
                key = key[..^suffix.Length];
#endif
            }
        }

        return key;
    }
}
