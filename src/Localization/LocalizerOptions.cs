using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.MessageFormat;
using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Configuration for a <see cref="DefaultLocalizer"/>: where to load catalogs from, the source language, the
/// translation formats, and the missing-argument and hot-reload behaviour. Everything is configured here —
/// there is no runtime mutation surface; to add a provider or source, build new options (<c>with</c>) and reconfigure.
/// </summary>
public sealed record LocalizerOptions
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
    /// Whether to load every catalog up front (<see cref="CultureLoading.Eager"/>) or each culture on first use
    /// (<see cref="CultureLoading.OnDemand"/>). On-demand keeps a single-user client (CLI, desktop, Blazor) to just
    /// the active language and pulls another in — live, without a restart — only on a switch to it; eager suits a
    /// server that handles many cultures at once. Defaults by platform: on-demand in the browser (Blazor
    /// WebAssembly), eager elsewhere. Override to force either.
    /// </summary>
    public CultureLoading CultureLoading { get; init; } =
        OperatingSystem.IsBrowser() ? CultureLoading.OnDemand : CultureLoading.Eager;

    /// <summary>Whether to watch the directory and reload on change. Off by default.</summary>
    public bool EnableHotReload { get; init; }

    /// <summary>How long to wait for changes to settle before reloading when hot reload is enabled.</summary>
    public TimeSpan HotReloadDebounce { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How to handle a referenced argument with no supplied value.</summary>
    public MissingArgumentPolicy MissingArguments { get; init; } = MissingArgumentPolicy.PassThrough;

    /// <summary>
    /// Catalog providers to load from, as factories over the resolved options — so a provider reads the configured
    /// <see cref="Formats"/>, <see cref="TranslationsDirectory"/>, and the rest at the moment it is built. Layered
    /// beneath the built-in directory provider (and, for the ambient store, the resource provider). An already-built
    /// provider is a trivial factory, <c>_ => provider</c>; one that needs wiring reads it off the options,
    /// <c>o =&gt; new MyProvider(o.Formats)</c>. Empty by default.
    /// </summary>
    public IReadOnlyList<Func<LocalizerOptions, ICatalogProvider>> Providers { get; init; } = [];

    /// <summary>
    /// The translation formats the catalog providers parse with — the parser set a catalog's bytes are read against.
    /// Defaults to the built-in formats (XLIFF, ARB, PO). Register an extra format on a copy to teach the providers a
    /// custom one; a provider from <see cref="Providers"/> reads this when it is built.
    /// </summary>
    public TranslationFormatRegistry Formats { get; init; } = BuiltInTranslationFormats.CreateRegistry();

    private static string DefaultDirectory() => Path.Combine(AppContext.BaseDirectory, "Translations");
}
