namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The single convention for the on-disk identity of a catalog entry: the translation category qualifies
/// the key so an entry is unique across categories (and contexts) in one file. Shared by every format
/// provider and the tooling so what is written can be read back exactly. The category is always present —
/// it is a first-class part of the identity, never dropped — so a global (uncategorized) entry is written
/// with an empty category, i.e. a leading <c>::</c>.
/// </summary>
public static class QualifiedKey
{
    private const string CategorySeparator = "::";

    /// <summary>
    /// Qualifies <paramref name="key"/> with its <paramref name="category"/> (and <paramref name="context"/>
    /// when present) into the on-disk identity — for example <c>Acme.Labels::save</c>, <c>::greeting</c>
    /// (global), or <c>Acme.Menu::post (#verb)</c>.
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

        var qualified = (category ?? string.Empty) + CategorySeparator + key;
        return string.IsNullOrEmpty(context) ? qualified : qualified + " (#" + context + ")";
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
