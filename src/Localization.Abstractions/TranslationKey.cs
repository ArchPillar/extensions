namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The single convention for combining a <c>Key</c> and optional <c>Context</c> into one composite
/// lookup key, shared by every format provider and the runtime so storage and lookup agree exactly.
/// </summary>
public static class TranslationKey
{
    /// <summary>
    /// The separator between context and key in a composite key (the gettext <c>EOT</c> convention).
    /// </summary>
    public const char Separator = '\u0004';

    /// <summary>
    /// Composes <paramref name="key"/> and <paramref name="context"/> into a single composite key. When
    /// no context is present, the composite key is the key itself.
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
}
