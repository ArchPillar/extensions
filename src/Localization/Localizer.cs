using System.Globalization;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The process-wide ambient localization entry point — a thin static facade over a single
/// <see cref="LocalizationContext"/> (the ambient one), so a string localizes from anywhere with no services,
/// including an exception thrown before any container exists. The ambient context is created lazily on first
/// use. For an isolated environment (parallel tests, multi-tenant hosting), construct a
/// <see cref="LocalizationContext"/> directly instead of using this facade.
/// </summary>
public static class Localizer
{
    private static readonly object _gate = new();
    private static LocalizationContext? _ambient;

    /// <summary>The global-namespace ambient localizer (the uncategorized bucket).</summary>
    public static ILocalizer Default => Ambient.Default;

    /// <summary>The single process-wide ambient <see cref="LocalizationContext"/>, created on first use.</summary>
    public static LocalizationContext Ambient
    {
        get
        {
            LocalizationContext? ambient = Volatile.Read(ref _ambient);
            return ambient ?? EnsureAmbient();
        }
    }

    /// <summary>
    /// Feeds initial <paramref name="options"/> to the ambient context, applied in one rebuild, and — when
    /// <paramref name="eager"/> is set — loads the catalogs now instead of lazily on first use.
    /// </summary>
    /// <param name="options">The configuration to apply.</param>
    /// <param name="eager">Whether to load the catalogs (directory + assembly discovery) up front.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static void Initialize(LocalizerOptions options, bool eager = false)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Ambient.Configure(options);
        if (eager)
        {
            Ambient.Load();
        }
    }

    /// <summary>
    /// Returns the ambient localizer scoped to <typeparamref name="T"/>'s full name (the <c>ILogger&lt;T&gt;</c>
    /// model), reading the latest ambient store on every call.
    /// </summary>
    /// <typeparam name="T">The type whose full name is the translation category.</typeparam>
    /// <returns>The ambient category-scoped localizer.</returns>
    public static ILocalizer<T> For<T>() => Ambient.For<T>();

    /// <summary>
    /// Returns the ambient localizer scoped to <paramref name="category"/> — the dynamic-category parallel of
    /// <see cref="For{T}"/>, for a category computed at runtime rather than a type argument.
    /// </summary>
    /// <param name="category">The translation category.</param>
    /// <returns>The ambient category-scoped localizer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public static ILocalizer ForCategory(string category) => Ambient.ForCategory(category);

    /// <summary>
    /// Translates <paramref name="key"/> through the global ambient store, falling back to
    /// <paramref name="defaultMessage"/>. The free-function form for
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
        Ambient.Translate(key, defaultMessage, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> with a disambiguation <paramref name="context"/> through the global
    /// ambient store, falling back to <paramref name="defaultMessage"/>.
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
        Ambient.Translate(key, defaultMessage, context, arguments);

    /// <summary>Layers a catalog into the ambient store as a host source (a later source wins).</summary>
    /// <param name="catalog">The catalog to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is <see langword="null"/>.</exception>
    public static void AddCatalog(Catalog catalog) => Ambient.AddCatalog(catalog);

    /// <summary>Layers a dynamic source into the ambient store (a later source wins).</summary>
    /// <param name="source">The source to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static void AddSource(ITranslationSource source) => Ambient.AddSource(source);

    /// <summary>Raised after any commit that changed the ambient store's merged snapshot — a background
    /// asynchronous load landing, a watched catalog reloading. A UI layer subscribes to re-render.</summary>
    public static event Action? CatalogsChanged
    {
        add => Ambient.CatalogsChanged += value;
        remove => Ambient.CatalogsChanged -= value;
    }

    /// <summary>The source language the ambient catalogs are written in (the configured
    /// <see cref="LocalizerOptions.SourceCulture"/>, defaulting to <c>en</c>) — the language whose strings appear
    /// in code as defaults. A host registering a culture-scoped catalog provider reads it so the fetch still pulls
    /// the source-language overrides.</summary>
    public static string SourceCultureName => Ambient.SourceCultureName;

    /// <summary>Registers a catalog provider with the ambient store at runtime (kept across a reconfigure). A
    /// host with no readable file system (Blazor WebAssembly) adds an HTTP <see cref="ManifestCatalogProvider"/>
    /// — created through <see cref="ManifestCatalogProvider.CreateAsync"/> — this way.</summary>
    /// <param name="provider">The catalog provider to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public static void AddProvider(ICatalogProvider provider) => Ambient.AddProvider(provider);

    /// <summary>
    /// Loads the catalogs for <paramref name="culture"/> from every registered provider — awaiting the
    /// asynchronous ones (an HTTP manifest) the synchronous on-demand path can only queue. Await it before the UI
    /// renders the culture; the subsequent synchronous lookups then resolve an already-loaded snapshot. Loads
    /// catalogs only — the active culture is the caller's concern.
    /// </summary>
    /// <param name="culture">The culture whose catalogs to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the culture's catalogs are loaded and committed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public static Task LoadCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default) =>
        Ambient.LoadCultureAsync(culture, cancellationToken);

    /// <summary>Loads every known culture's catalogs from every registered provider — the awaited "load
    /// everything" for an asynchronous context (server startup).</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when all known cultures' catalogs are loaded and committed.</returns>
    public static Task PreloadAllAsync(CancellationToken cancellationToken = default) =>
        Ambient.PreloadAllAsync(cancellationToken);

    /// <summary>Applies the configuration to the ambient store in one rebuild.</summary>
    /// <param name="options">The configuration to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static void Configure(LocalizerOptions options) => Ambient.Configure(options);

    /// <summary>
    /// Clears all layered catalogs, sources, and discovery state from the ambient store. Intended for test
    /// isolation against the shared ambient state.
    /// </summary>
    public static void Reset() => Ambient.Reset();

    // The internal lookups the IStringLocalizer adapter composes over, on the ambient context.
    internal static string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        (string Name, object? Value)[] arguments,
        out bool overrideFound) =>
        Ambient.TranslateInCategory(category, key, defaultMessage, arguments, out overrideFound);

    internal static string? TranslateOverride(string category, string key, (string Name, object? Value)[] arguments) =>
        Ambient.TranslateOverride(category, key, arguments);

    internal static IReadOnlyList<KeyValuePair<string, string>> EnumerateOverrides(string category, bool includeParentCultures) =>
        Ambient.EnumerateOverrides(category, includeParentCultures);

    // Test-only: returns the static facade to a pristine state — disposes and clears the ambient context — so a
    // test that configures the ambient does not leak into the next.
    internal static void ResetAmbientForTests()
    {
        lock (_gate)
        {
            _ambient?.Dispose();
            _ambient = null;
        }
    }

    private static LocalizationContext EnsureAmbient()
    {
        lock (_gate)
        {
            return _ambient ??= LocalizationContext.CreateAmbient();
        }
    }
}
