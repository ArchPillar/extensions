using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The process-wide ambient translation store, modeled on <c>IConfiguration</c>: one store built from
/// layered sources where a later source wins, reachable with no services so a string can be localized
/// from anywhere — including an exception message thrown before any container exists. A library's embedded
/// catalogs and a host's overrides are both just sources layered in. Embedded catalogs are discovered
/// lazily as assemblies load (an assembly cannot run a translatable call before it is loaded), so nothing
/// has to be configured for a library's translations to work. This is a thin facade: the catalogs and the
/// merge live in a global <see cref="CatalogStore"/>; the facade resolves against its snapshot and exposes
/// the ambient calls and configuration.
/// </summary>
public static class Localizer
{
    private static readonly CatalogStore _store = CatalogStore.CreateAmbient();

    /// <summary>The global-namespace ambient localizer (the uncategorized bucket).</summary>
    public static ILocalizer Default { get; } = new Internal.AmbientLocalizer();

    /// <summary>The source culture used to render in-code defaults; defaults to <c>en</c>.</summary>
    public static string SourceCulture
    {
        get => _store.SourceCulture;
        set => _store.SourceCulture = value;
    }

    /// <summary>
    /// How the formatter handles a message that references an argument no value was supplied for; defaults
    /// to <see cref="MissingArgumentPolicy.PassThrough"/>. Applied to every ambient-resolved translation, so
    /// an injected interface and a non-DI caller share the same policy as a directly constructed localizer.
    /// </summary>
    public static MissingArgumentPolicy MissingArguments
    {
        get => _store.MissingArguments;
        set => _store.MissingArguments = value;
    }

    /// <summary>
    /// Applies the source culture, missing-argument policy, translations directory, and any additional sources
    /// from <paramref name="options"/> to the ambient store in one go (a single rebuild) — the single entry
    /// point for configuring the global store, used by <c>AddArchPillarLocalization</c>.
    /// </summary>
    /// <param name="options">The configuration to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static void Configure(LocalizerOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _store.Configure(options);
    }

    /// <summary>
    /// The directory loaded by default (the dev/default "files beside the binary" path), defaulting to a
    /// <c>Translations</c> folder next to the application binary. Catalogs found here layer below host
    /// additions and above embedded catalogs. Setting it reloads the directory layer.
    /// </summary>
    public static string TranslationsDirectory
    {
        get => _store.TranslationsDirectory;
        set => _store.TranslationsDirectory = value;
    }

    /// <summary>
    /// Returns the ambient localizer scoped to <typeparamref name="T"/>'s full name, reading the latest
    /// store on every call.
    /// </summary>
    /// <typeparam name="T">The type whose full name is the translation category.</typeparam>
    /// <returns>The ambient category-scoped localizer.</returns>
    public static ILocalizer<T> For<T>() => new Internal.AmbientCategoryLocalizer<T>();

    /// <summary>
    /// Translates <paramref name="key"/> through the global ambient store, falling back to
    /// <paramref name="defaultMessage"/>. The free-function form of <see cref="Default"/>, for
    /// <c>using static ArchPillar.Extensions.Localization.Localizer;</c> — the call site then reads
    /// <c>Translate("greeting", "Hello {name}!", ("name", name))</c> with no receiver.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public static string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments) =>
        Default.Translate(key, defaultMessage, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> with a disambiguation <paramref name="context"/> through the global
    /// ambient store, falling back to <paramref name="defaultMessage"/>. The free-function form of
    /// <see cref="Default"/> for <c>using static</c>.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="context">The disambiguation context.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public static string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string context,
        params (string Name, object? Value)[] arguments) =>
        Default.Translate(key, defaultMessage, context, arguments);

    /// <summary>
    /// Layers a catalog into the store as a host source (a later source wins). Use this to override or add
    /// translations from the host.
    /// </summary>
    /// <param name="catalog">The catalog to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is <see langword="null"/>.</exception>
    public static void AddCatalog(Catalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        _store.AddCatalog(catalog);
    }

    /// <summary>
    /// Layers a dynamic source into the store (a later source wins) — for example a pseudo-localization
    /// source.
    /// </summary>
    /// <param name="source">The source to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static void AddSource(ITranslationSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        _store.AddSource(source);
    }

    /// <summary>
    /// Clears all layered catalogs, sources, and discovery state, returning the store to empty. Intended
    /// for test isolation against the shared ambient state.
    /// </summary>
    public static void Reset() => _store.Reset();

    // The single localizer over the global store, built once and reading its layers and rendering context
    // live, so every configuration change is observed through the store with no rebuild.
    internal static DefaultLocalizer Current { get; } = new(_store);

    internal static void EnsureCulture(CultureInfo culture) => _store.EnsureCulture(culture);

    // The found-aware ambient lookup the IStringLocalizer adapter composes over: resolves within
    // <paramref name="category"/> for the current UI culture and reports whether a loaded override was used,
    // so the adapter can fall through to a previously-registered factory on a miss before using the default.
    internal static string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        (string Name, object? Value)[] arguments,
        out bool overrideFound)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        EnsureCulture(culture);
        return Current.TranslateInCategory(category, key, defaultMessage, context: null, out overrideFound, arguments);
    }

    // The override-or-null ambient lookup the IStringLocalizer adapter uses so it never renders the name as a
    // default before consulting the inner factory (the name may be ResourceManager composite-format text).
    internal static string? TranslateOverride(string category, string key, (string Name, object? Value)[] arguments)
    {
        EnsureCulture(CultureInfo.CurrentUICulture);
        return Current.TranslateOverride(category, key, context: null, arguments);
    }

    // Enumerates the loaded overrides for a category in the current UI culture, so the IStringLocalizer
    // adapter's GetAllStrings includes ambient entries rather than only the inner factory's.
    internal static IReadOnlyList<KeyValuePair<string, string>> EnumerateOverrides(string category, bool includeParentCultures)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        EnsureCulture(culture);
        return Current.EnumerateCategory(culture, category, includeParentCultures);
    }
}
