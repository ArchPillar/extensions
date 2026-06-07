using System.Globalization;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A dynamic translation source consulted by the localizer before it falls back to the in-code default.
/// Unlike a <see cref="Catalog"/> (a fixed set of entries), a source can compute a message per lookup —
/// for example a pseudo-localization source that transforms the default for QA, or a source backed by a
/// live translation service. Sources are layered: a later source wins, and returning <see langword="null"/>
/// defers to the next source and finally to the in-code default. This is the public extension point for
/// custom providers.
/// </summary>
public interface ITranslationSource
{
    /// <summary>
    /// Resolves the override for a lookup, or <see langword="null"/> to defer to the next source.
    /// </summary>
    /// <param name="culture">The requested culture.</param>
    /// <param name="category">The translation category (namespace); empty for the global namespace.</param>
    /// <param name="key">The composite lookup key (context-composed).</param>
    /// <param name="defaultMessage">The in-code source default, supplied so a source may transform it.</param>
    /// <returns>The override message, or <see langword="null"/> to defer.</returns>
    public string? Resolve(CultureInfo culture, string category, string key, string defaultMessage);
}
