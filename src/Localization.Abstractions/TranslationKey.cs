namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The one owner of the in-memory composite-key conventions, all built on a single control-character
/// <see cref="Separator"/> (the gettext <c>EOT</c> convention) so every producer and consumer agrees
/// byte-for-byte. <see cref="Compose"/> joins a key and optional context into the lookup key used
/// <em>within</em> a category (what the runtime snapshot is keyed by); <see cref="ComposeQualified"/>
/// prefixes the category as well, for an identity unique <em>across</em> categories (what extraction and
/// reconciliation dedupe by). This is the in-memory composite identity — distinct from the human-facing
/// on-disk member name a flat format like ARB encodes when it serializes an entry.
/// </summary>
public static class TranslationKey
{
    /// <summary>
    /// The separator between the parts of a composite key (the gettext <c>EOT</c> convention).
    /// </summary>
    public const char Separator = '\u0004';

    /// <summary>
    /// Composes <paramref name="key"/> and <paramref name="context"/> into a single composite key, unique
    /// within a category. When no context is present, the composite key is the key itself.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <returns>The composite key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static string Compose(string key, string? context)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return string.IsNullOrEmpty(context) ? key : context + Separator + key;
    }

    /// <summary>
    /// Splits a composite key produced by <see cref="Compose"/> back into its key and optional context. The
    /// inverse of <see cref="Compose"/>: a composite with no <see cref="Separator"/> is a bare key with no
    /// context.
    /// </summary>
    /// <param name="composite">The composite key to split.</param>
    /// <returns>The key and its context, or <see langword="null"/> context when the composite carries none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="composite"/> is <see langword="null"/>.</exception>
    public static (string Key, string? Context) Decompose(string composite)
    {
        if (composite is null)
        {
            throw new ArgumentNullException(nameof(composite));
        }

        var separator = composite.IndexOf(Separator);
        if (separator < 0)
        {
            return (composite, null);
        }

#if NETSTANDARD2_0
        return (composite.Substring(separator + 1), composite.Substring(0, separator));
#else
        return (composite[(separator + 1)..], composite[..separator]);
#endif
    }

    /// <summary>
    /// Composes the <em>category-qualified</em> identity of a translation site — <paramref name="category"/>
    /// prefixed onto <see cref="Compose"/> of <paramref name="key"/> and <paramref name="context"/> — so the
    /// same key under two categories is two distinct identities. This is the identity extraction and
    /// reconciliation dedupe by; the analyzer and the tool share it so they agree byte-for-byte.
    /// </summary>
    /// <param name="category">The translation category (empty for the global namespace).</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <returns>The category-qualified composite identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static string ComposeQualified(string category, string key, string? context)
    {
        return (category ?? string.Empty) + Separator + Compose(key, context);
    }
}
