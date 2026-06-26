using System.Globalization;
using ArchPillar.Extensions.Localization.Catalogs;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Renders translatable call sites at runtime: looks up the loaded override for the requested culture
/// and key, falls back through parent cultures to the in-code default, and formats with the ICU engine. A
/// pure resolution engine — it resolves against the snapshot and rendering context of a live
/// <see cref="CatalogStore"/> and owns no I/O. Lookups are lock-free; designed to be a singleton and safe for
/// concurrent use.
/// </summary>
public sealed class DefaultLocalizer : ILocalizer
{
    private readonly CatalogStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultLocalizer"/> class over a <see cref="CatalogStore"/>,
    /// resolving against the store's current snapshot and rendering through its <see cref="CatalogStore.Context"/>,
    /// both read live so a reload or configuration change is observed on the next lookup. The store is owned by
    /// the caller; the localizer only reads it.
    /// </summary>
    /// <param name="store">The catalogue store to resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    internal DefaultLocalizer(CatalogStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
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
        TranslateCore(CultureInfo.CurrentUICulture, key, defaultMessage, context: null, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> with a disambiguation <paramref name="context"/> for the current
    /// UI culture, falling back to <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="context">The disambiguation context.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string context,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(CultureInfo.CurrentUICulture, key, defaultMessage, context, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> for an explicit <paramref name="culture"/>, falling back through
    /// parent cultures to <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="culture">The culture to translate for.</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public string Translate(
        CultureInfo culture,
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string? context,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(culture, key, defaultMessage, context, arguments);

    /// <summary>
    /// Translates for an explicit culture and additionally reports whether a loaded override was used
    /// (rather than the in-code default). Intended for integration adapters such as <c>IStringLocalizer</c>.
    /// </summary>
    /// <param name="culture">The culture to translate for.</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The fallback rendered when no override exists.</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <param name="overrideFound">Set to <see langword="true"/> when a loaded override was used.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public string Translate(
        CultureInfo culture,
        string key,
        string defaultMessage,
        string? context,
        out bool overrideFound,
        params (string Name, object? Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var composite = TranslationKey.Compose(key, context);
        var message = _store.Lookup(culture, category: string.Empty, composite);
        overrideFound = message is not null;

        // An override was authored for the requested culture, so render it with that culture's rules.
        // The in-code default is source-language text, so render it with the source culture's rules —
        // otherwise an English default shown under, say, Japanese rules would pluralize incorrectly.
        RenderingContext rendering = CurrentContext();
        return message is not null
            ? rendering.Formatter.Format(message, culture, arguments)
            : rendering.Formatter.Format(defaultMessage, rendering.SourceCulture, arguments);
    }

    // The category-scoped core used by ILocalizer<T> (via the factory). It looks the key up within the
    // localizer's category for the current UI culture, falling back to the in-code default. A literal
    // lookup with no context allocates nothing: the composite key is the key itself and the tiered
    // dictionary reads do not allocate.
    internal string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        string? context,
        (string Name, object? Value)[] arguments) =>
        TranslateInCategory(category, key, defaultMessage, context, out _, arguments);

    // The found-aware, category-scoped core used by the IStringLocalizer adapter so it can compose: a hit
    // resolves from the store, a miss is reported so the adapter can fall through to a previously-registered
    // factory (the .resx-backed one) before settling on the in-code default.
    internal string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        string? context,
        out bool overrideFound,
        (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        var composite = TranslationKey.Compose(key, context);
        var message = _store.Lookup(culture, category, composite);
        overrideFound = message is not null;
        RenderingContext rendering = CurrentContext();
        return message is not null
            ? rendering.Formatter.Format(message, culture, arguments)
            : rendering.Formatter.Format(defaultMessage, rendering.SourceCulture, arguments);
    }

    // Resolves and formats a loaded override (or a source result) within a category for the current UI
    // culture, or returns null when there is none. Unlike TranslateInCategory it never renders a default —
    // the IStringLocalizer adapter uses this so a miss does not push the name (which may be ResourceManager
    // composite-format text like "{0:C}", not ICU) through the ICU formatter before the inner factory is tried.
    internal string? TranslateOverride(
        string category,
        string key,
        string? context,
        (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        var composite = TranslationKey.Compose(key, context);
        var message = _store.Lookup(culture, category, composite);
        return message is null ? null : CurrentContext().Formatter.Format(message, culture, arguments);
    }

    // Enumerates the loaded overrides for a category in the given culture as (compositeKey, message) pairs — the
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
        string? context,
        (string Name, object? Value)[] arguments) =>
        Translate(culture, key, defaultMessage, context, out _, arguments);

    // The rendering context: the store's live context, so a configuration change is observed immediately and the
    // formatter instance is shared.
    private RenderingContext CurrentContext() => _store.Context;
}
