namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The one owner of the category-qualified identity convention. <see cref="ComposeQualified"/> prefixes a
/// key with its category — joined by a single control-character <see cref="Separator"/> (the gettext
/// <c>EOT</c> convention) — so the same key under two categories is two distinct identities. This is the
/// identity extraction and reconciliation dedupe by; the analyzer and the tool share it so they agree
/// byte-for-byte.
/// </summary>
public static class TranslationKey
{
    /// <summary>
    /// The separator between the category and the key of a qualified identity (the gettext <c>EOT</c> convention).
    /// </summary>
    public const char Separator = '\u0004';

    /// <summary>
    /// Composes the category-qualified identity of a translation site — <paramref name="category"/>
    /// prefixed onto <paramref name="key"/> — so the same key under two categories is two distinct
    /// identities.
    /// </summary>
    /// <param name="category">The translation category (empty for the global namespace).</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <returns>The category-qualified identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static string ComposeQualified(string category, string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return (category ?? string.Empty) + Separator + key;
    }
}
