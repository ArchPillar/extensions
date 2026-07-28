using System.Globalization;
using ArchPillar.Extensions.Localization.Catalogs;
using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Renders translatable call sites at runtime: looks up the loaded override for the requested culture
/// and key, falls back through parent cultures to the in-code default, and formats with the ICU engine. A
/// pure resolution engine — it resolves against the snapshot of a live <see cref="CatalogStore"/> and renders
/// through its own <see cref="RenderingContext"/>, and owns no I/O. Lookups are lock-free; designed to be a
/// singleton and safe for concurrent use.
/// </summary>
internal sealed class DefaultLocalizer : ILocalizer
{
    private readonly CatalogStore _store;
    private volatile RenderingContext _rendering;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultLocalizer"/> class over a <see cref="CatalogStore"/>
    /// and a <see cref="RenderingContext"/>: it resolves overrides against the store's current snapshot (read live,
    /// so a reload is observed on the next lookup) and renders through the rendering context, which
    /// <see cref="Reconfigure"/> swaps on a configuration change. The store is owned by the caller; the localizer
    /// only reads it.
    /// </summary>
    /// <param name="store">The catalogue store to resolve against.</param>
    /// <param name="rendering">The rendering context — source culture, missing-argument policy, and the ICU formatter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    internal DefaultLocalizer(CatalogStore store, RenderingContext rendering)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _rendering = rendering;
    }

    /// <summary>
    /// Translates <paramref name="key"/> for the current UI culture, falling back to
    /// <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(CultureInfo.CurrentUICulture, key, defaultMessage, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> for an explicit <paramref name="culture"/>, falling back through
    /// parent cultures to <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="culture">The culture to translate for.</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public string Translate(
        CultureInfo culture,
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(culture, key, defaultMessage, arguments);

    /// <summary>
    /// Translates for an explicit culture and additionally reports whether a loaded override was used
    /// (rather than the in-code default). Intended for integration adapters such as <c>IStringLocalizer</c>.
    /// </summary>
    /// <param name="culture">The culture to translate for.</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The fallback rendered when no override exists.</param>
    /// <param name="overrideFound">Set to <see langword="true"/> when a loaded override was used.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public string Translate(
        CultureInfo culture,
        string key,
        string defaultMessage,
        out bool overrideFound,
        params (string Name, object? Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var message = _store.Lookup(culture, category: string.Empty, key);

        // An override was authored for the requested culture, so render it with that culture's rules.
        // The in-code default is source-language text, so render it with the source culture's rules —
        // otherwise an English default shown under, say, Japanese rules would pluralize incorrectly.
        RenderingContext rendering = CurrentContext();
        var rendered = TryRenderOverride(message, culture, rendering, arguments);
        overrideFound = rendered is not null;
        return rendered ?? rendering.Formatter.Format(defaultMessage, rendering.SourceCulture, arguments);
    }

    // The category-scoped core used by ILocalizer<T> (via the factory). It looks the key up within the
    // localizer's category for the current UI culture, falling back to the in-code default. A literal
    // lookup allocates nothing: the tiered dictionary reads do not allocate.
    internal string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        (string Name, object? Value)[] arguments) =>
        TranslateInCategory(category, key, defaultMessage, out _, arguments);

    // The found-aware, category-scoped core used by the IStringLocalizer adapter so it can compose: a hit
    // resolves from the store, a miss is reported so the adapter can fall through to a previously-registered
    // factory (the .resx-backed one) before settling on the in-code default.
    internal string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        out bool overrideFound,
        (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        var message = _store.Lookup(culture, category, key);
        RenderingContext rendering = CurrentContext();
        var rendered = TryRenderOverride(message, culture, rendering, arguments);
        overrideFound = rendered is not null;
        return rendered ?? rendering.Formatter.Format(defaultMessage, rendering.SourceCulture, arguments);
    }

    // Resolves and formats a loaded override (or a source result) within a category for the current UI
    // culture, or returns null when there is none. Unlike TranslateInCategory it never renders a default —
    // the IStringLocalizer adapter uses this so a miss does not push the name (which may be ResourceManager
    // composite-format text like "{0:C}", not ICU) through the ICU formatter before the inner factory is tried.
    internal string? TranslateOverride(
        string category,
        string key,
        (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        var message = _store.Lookup(culture, category, key);
        return TryRenderOverride(message, culture, CurrentContext(), arguments);
    }

    // Enumerates the loaded overrides for a category in the given culture as (key, message) pairs — the
    // IStringLocalizer adapter's GetAllStrings reads this so ambient entries are listed, not just the inner factory's.
    // Delegates to the store, which owns the snapshot; parents merge most-specific-wins when included.
    internal IReadOnlyList<KeyValuePair<string, string>> EnumerateCategory(CultureInfo culture, string category, bool includeParentCultures) =>
        _store.EnumerateCategory(culture, category, includeParentCultures);

    // The non-attributed core. The public overloads carry the attributes so the extractor finds every
    // call site; they delegate here so the library's own forwarding never looks like a translation site.
    private string TranslateCore(
        CultureInfo culture,
        string key,
        string defaultMessage,
        (string Name, object? Value)[] arguments) =>
        Translate(culture, key, defaultMessage, out _, arguments);

    // Renders a loaded override, or returns null when there is none, or when it fails to render. A catalog can
    // ship a well-formed file whose message VALUE is invalid ICU (an unbalanced brace, a bad plural clause); a
    // malformed override is treated exactly like a missing one, so every call site's existing "no override"
    // handling — render the in-code default, or (for TranslateOverride) report null onward — degrades it
    // gracefully with no code duplicated per site. Only the override render is ever caught: the in-code default
    // is build-validated by the analyzer, so a default that fails to parse is a developer bug that must still
    // surface, never be swallowed here.
    private static string? TryRenderOverride(
        string? overrideMessage,
        CultureInfo culture,
        RenderingContext rendering,
        (string Name, object? Value)[] arguments)
    {
        if (overrideMessage is null)
        {
            return null;
        }

        try
        {
            return rendering.Formatter.Format(overrideMessage, culture, arguments);
        }
        catch (MessageFormatException)
        {
            return null;
        }
    }

    // The live rendering context, swapped by Reconfigure on a configuration change so the formatter and source
    // culture are observed on the next lookup.
    private RenderingContext CurrentContext() => _rendering;

    // Applies a new rendering context after a reconfigure (the owning context re-derives it from the options).
    internal void Reconfigure(RenderingContext rendering) => _rendering = rendering;

    // The source language the in-code defaults are written in, for the owning context/ambient facade to surface.
    internal string SourceCultureName => _rendering.SourceCultureName;
}
