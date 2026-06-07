namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Migration markers: no-op helpers that mark a string literal for extraction without changing what the
/// program does. They exist so a codebase adopting this library can surface strings that never flow through
/// a localizer — log messages, <c>throw new(...)</c> text, anything inline — to translators with a single
/// token, instead of a rewrite. Import statically (<c>using static ArchPillar.Extensions.Localization.TranslationMarkers;</c>)
/// and the call site reads simply <c>L("Email is required")</c>.
/// </summary>
public static class TranslationMarkers
{
    /// <summary>
    /// Marks <paramref name="text"/> for extraction and returns it unchanged. The literal becomes both the
    /// key and the in-code default under the global category, so it appears in the catalog for translators;
    /// at runtime this does nothing but hand back the same string (no setup, no ambient lookup). Convert the
    /// call to the native <c>ILocalizer</c> API when you want the site to actually resolve overrides.
    /// </summary>
    /// <param name="text">The source-language literal to extract (ICU MessageFormat).</param>
    /// <returns><paramref name="text"/>, unchanged.</returns>
    public static string L([Translatable, TranslationDefault] string text) => text;
}
