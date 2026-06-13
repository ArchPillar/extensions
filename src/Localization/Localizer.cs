
namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The process-wide ambient localization entry point — a thin static facade over a single
/// <see cref="LocalizationContext"/> (the ambient one), so a string localizes from anywhere with no services,
/// including an exception thrown before any container exists. The ambient context is created lazily on first
/// use; call <see cref="Disable"/> to forbid it entirely (any later use throws), which gives test isolation
/// and a fully static-free option — construct or DI-register a <see cref="LocalizationContext"/> instead.
/// </summary>
public static class Localizer
{
    private static readonly object _gate = new();
    private static LocalizationContext? _ambient;
    private static bool _disabled;

    /// <summary>The global-namespace ambient localizer (the uncategorized bucket).</summary>
    public static ILocalizer Default => Ambient.Default;

    /// <summary>
    /// The single process-wide ambient <see cref="LocalizationContext"/>, created on first use. Throws if the
    /// ambient context has been disabled via <see cref="Disable"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ambient context is disabled.</exception>
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
    /// <exception cref="InvalidOperationException">The ambient context is disabled.</exception>
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
    /// Disables the process-wide ambient context: any subsequent attempt to use the static
    /// <see cref="Localizer"/> throws. Call it before first use (it throws if the ambient context is already
    /// initialized). Use a directly constructed or DI-registered <see cref="LocalizationContext"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ambient context is already initialized.</exception>
    public static void Disable()
    {
        lock (_gate)
        {
            if (_ambient is not null)
            {
                throw new InvalidOperationException("The ambient localization context is already initialized; disable it before first use.");
            }

            _disabled = true;
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

    // Test-only: returns the static facade to a pristine state — disposes and clears the ambient context and
    // clears the disabled flag — so a lifecycle test (Disable/Initialize) does not leak into the next test.
    internal static void ResetAmbientForTests()
    {
        lock (_gate)
        {
            _ambient?.Dispose();
            _ambient = null;
            _disabled = false;
        }
    }

    private static LocalizationContext EnsureAmbient()
    {
        lock (_gate)
        {
            if (_disabled)
            {
                throw new InvalidOperationException(
                    "The ambient localization context is disabled. Construct a LocalizationContext, or inject one registered in DI, instead of using the static Localizer.");
            }

            return _ambient ??= LocalizationContext.CreateAmbient();
        }
    }
}
