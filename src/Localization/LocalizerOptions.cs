using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Configuration for a <see cref="DefaultLocalizer"/>: where to load catalogs from, the source language, the
/// format precedence on overlap, and the missing-argument and hot-reload behaviour.
/// </summary>
public sealed class LocalizerOptions
{
    /// <summary>
    /// The directory containing translation catalog files. Defaults to a <c>Translations</c> directory
    /// beside the application binary.
    /// </summary>
    public string TranslationsDirectory { get; init; } = DefaultDirectory();

    /// <summary>
    /// The language the in-code defaults are written in. Its catalog loads as an override layer like any other
    /// culture (only genuine overrides survive; an echo of the in-code default does not), and it always bypasses
    /// the <see cref="Cultures"/> allow-list. The in-code default remains the terminal fallback beneath it.
    /// </summary>
    public string SourceCulture { get; init; } = "en";

    /// <summary>
    /// The target cultures to load; <see langword="null"/> discovers every culture present in the directory.
    /// The <see cref="SourceCulture"/> is always loaded regardless of this list.
    /// </summary>
    public IReadOnlyList<string>? Cultures { get; init; }

    /// <summary>
    /// The format preference when the same culture and key appear in more than one file. Earlier entries
    /// win; the default prefers the ICU-native formats over Portable Object.
    /// </summary>
    public IReadOnlyList<string> FormatPrecedence { get; init; } = ["xliff", "arb", "po"];

    /// <summary>Whether to watch the directory and reload on change. Off by default.</summary>
    public bool EnableHotReload { get; init; }

    /// <summary>How long to wait for changes to settle before reloading when hot reload is enabled.</summary>
    public TimeSpan HotReloadDebounce { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How to handle a referenced argument with no supplied value.</summary>
    public MissingArgumentPolicy MissingArguments { get; init; } = MissingArgumentPolicy.PassThrough;

    /// <summary>
    /// Custom translation sources, layered above the loaded catalogs (a later source wins) and resolved by the
    /// very same path — a source is just a catalog a user implements (<see cref="ITranslationSource"/>), so the
    /// merged catalog snapshot is itself the lowest such layer. Use for providers such as pseudo-localization
    /// or a live translation service. Empty by default.
    /// </summary>
    public IReadOnlyList<ITranslationSource> Sources { get; init; } = [];

    private static string DefaultDirectory() => Path.Combine(AppContext.BaseDirectory, "Translations");
}
