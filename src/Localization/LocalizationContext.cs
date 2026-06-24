using System.Globalization;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A self-contained localization environment: it owns a <see cref="CatalogStore"/> (the catalogs and their
/// merge) and the localizers over it, and exposes the call and configuration surface as an instance. Construct
/// one directly — or register it in DI — for a fully self-contained, static-free setup (ideal for tests, or
/// hosting more than one localization scope in a process). The process-wide ambient environment behind the
/// static <see cref="Localizer"/> is one of these.
/// </summary>
public sealed class LocalizationContext : IDisposable
{
    private readonly CatalogStore _store;

    /// <summary>
    /// Initializes a new, self-contained <see cref="LocalizationContext"/> over <paramref name="options"/> — a
    /// directory-backed store with no process-wide assembly discovery. Use this for DI or isolated scenarios.
    /// </summary>
    /// <param name="options">The configuration, or <see langword="null"/> for the defaults.</param>
    public LocalizationContext(LocalizerOptions? options = null)
        : this(options ?? new LocalizerOptions(), ambient: false)
    {
    }

    private LocalizationContext(LocalizerOptions options, bool ambient)
    {
        _store = ambient ? CatalogStore.CreateAmbient() : new CatalogStore(options);
        Engine = new DefaultLocalizer(_store);
        Default = new Internal.AmbientLocalizer(this);
    }

    /// <summary>Creates the process-wide ambient environment (its store discovers embedded and satellite
    /// catalogs as assemblies load, and reads its directory lazily on first use).</summary>
    internal static LocalizationContext CreateAmbient() => new(new LocalizerOptions(), ambient: true);

    /// <summary>Raised after any commit that changed the merged snapshot — a background asynchronous load
    /// landing, a watched catalog reloading. Forwarded from the store; a UI layer subscribes to re-render.</summary>
    public event Action? CatalogsChanged
    {
        add => _store.CatalogsChanged += value;
        remove => _store.CatalogsChanged -= value;
    }

    /// <summary>The global-namespace localizer (the uncategorized bucket) over this context.</summary>
    public ILocalizer Default { get; }

    /// <summary>Returns the localizer scoped to <typeparamref name="T"/>'s full name, over this context.</summary>
    /// <typeparam name="T">The type whose full name is the translation category.</typeparam>
    /// <returns>The category-scoped localizer.</returns>
    public ILocalizer<T> For<T>() => new Internal.AmbientCategoryLocalizer<T>(this);

    /// <summary>Returns the localizer scoped to <paramref name="category"/>, over this context — the
    /// dynamic-category parallel of <see cref="For{T}"/>, for a category computed at runtime (a model type's name,
    /// say) rather than a type argument.</summary>
    /// <param name="category">The translation category.</param>
    /// <returns>The category-scoped localizer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public ILocalizer ForCategory(string category)
    {
        if (category is null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        return new Internal.AmbientCategoryLocalizer(this, category);
    }

    /// <summary>Translates <paramref name="key"/> through this context's global bucket, falling back to
    /// <paramref name="defaultMessage"/> — the instance form of <see cref="Default"/>.</summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments) =>
        Default.Translate(key, defaultMessage, arguments);

    /// <summary>Translates <paramref name="key"/> with a disambiguation <paramref name="context"/> through this
    /// context's global bucket.</summary>
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
        Default.Translate(key, defaultMessage, context, arguments);

    /// <summary>Layers a catalog into the store as a host source (a later source wins).</summary>
    /// <param name="catalog">The catalog to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is <see langword="null"/>.</exception>
    public void AddCatalog(Catalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        _store.AddCatalog(catalog);
    }

    /// <summary>Layers a dynamic source into the store (a later source wins).</summary>
    /// <param name="source">The source to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public void AddSource(ITranslationSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        _store.AddSource(source);
    }

    /// <summary>Applies the configuration — source culture, missing-argument policy, translations directory, format
    /// precedence, culture loading, hot reload, the culture allow-list, and dynamic sources — in one rebuild.</summary>
    /// <param name="options">The configuration to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public void Configure(LocalizerOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _store.Configure(options);
    }

    /// <summary>Eagerly loads the catalogs now (otherwise the ambient store loads them lazily on first use).</summary>
    public void Load() => _store.EnsureStarted();

    /// <summary>
    /// Registers a catalog provider with the store at runtime, appended after the configured providers and kept
    /// across a reconfigure. A host with no readable file system (Blazor WebAssembly) registers an HTTP-backed
    /// provider this way; an asynchronous provider's catalogs are loaded through <see cref="LoadCultureAsync"/> or
    /// <see cref="PreloadAllAsync"/>.
    /// </summary>
    /// <param name="provider">The catalog provider to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public void AddProvider(ICatalogProvider provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        _store.AddProvider(provider);
    }

    /// <summary>
    /// Loads the catalogs for <paramref name="culture"/> from every registered provider — awaiting the
    /// asynchronous ones (an HTTP manifest, say) that the synchronous on-demand path can only queue. Await it
    /// before the UI renders the culture, so the subsequent synchronous lookups resolve an already-loaded snapshot
    /// with no flash. This loads catalogs only; the active culture is the caller's concern, untouched here.
    /// </summary>
    /// <param name="culture">The culture whose catalogs to load (its parent chain comes too).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the culture's catalogs are loaded and committed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public Task LoadCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return _store.LoadCultureAsync(culture, cancellationToken);
    }

    /// <summary>
    /// Loads every known culture's catalogs from every registered provider — the awaited "load everything" for an
    /// asynchronous context (server startup). Awaits the asynchronous providers and runs the synchronous ones, then
    /// commits once.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when all known cultures' catalogs are loaded and committed.</returns>
    public Task PreloadAllAsync(CancellationToken cancellationToken = default) =>
        _store.PreloadAllAsync(cancellationToken);

    /// <summary>Clears all layered catalogs, sources, and discovery state, returning the context to empty.</summary>
    public void Reset() => _store.Reset();

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();

    // The engine over this context's store, for the context's own localizers and the IStringLocalizer adapter.
    internal DefaultLocalizer Engine { get; }

    internal void EnsureCulture(CultureInfo culture) => _store.EnsureCulture(culture);

    /// <summary>The source language these catalogs are written in (the configured
    /// <see cref="LocalizerOptions.SourceCulture"/>, defaulting to <c>en</c>) — the language whose strings appear
    /// in code as defaults. A host registering a culture-scoped catalog provider reads it so the fetch still pulls
    /// the source-language overrides.</summary>
    public string SourceCultureName => _store.SourceCultureName;

    // The found-aware ambient lookup the IStringLocalizer adapter composes over.
    internal string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        (string Name, object? Value)[] arguments,
        out bool overrideFound)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        EnsureCulture(culture);
        return Engine.TranslateInCategory(category, key, defaultMessage, context: null, out overrideFound, arguments);
    }

    // The override-or-null lookup the IStringLocalizer adapter uses so a miss never renders the name.
    internal string? TranslateOverride(string category, string key, (string Name, object? Value)[] arguments)
    {
        EnsureCulture(CultureInfo.CurrentUICulture);
        return Engine.TranslateOverride(category, key, context: null, arguments);
    }

    // The loaded overrides for a category, so the IStringLocalizer adapter's GetAllStrings includes them.
    internal IReadOnlyList<KeyValuePair<string, string>> EnumerateOverrides(string category, bool includeParentCultures)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        EnsureCulture(culture);
        return Engine.EnumerateCategory(culture, category, includeParentCultures);
    }
}
