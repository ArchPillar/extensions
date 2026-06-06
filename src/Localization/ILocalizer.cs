namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Resolves translatable call sites at runtime: looks up the loaded override for the current UI culture
/// and the given key, falling back through parent cultures to the in-code default. The in-code default is
/// always the source of truth and the terminal fallback. An implementation has a fixed translation
/// category; <see cref="ILocalizer{T}"/> derives that category from its type argument, the
/// <c>ILogger&lt;T&gt;</c> way.
/// </summary>
public interface ILocalizer
{
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
        params (string Name, object? Value)[] arguments);

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
        params (string Name, object? Value)[] arguments);
}

/// <summary>
/// A category-scoped localizer whose translation category is the full type name of
/// <typeparamref name="T"/>, derived automatically — exactly as <c>ILogger&lt;T&gt;</c> derives its
/// category. There is nothing to configure: inject <c>ILocalizer&lt;MyComponent&gt;</c> and its keys live
/// under <c>MyComponent</c>. Shared strings live in their own scoped type (a shared class injected as
/// <c>ILocalizer&lt;SharedResource&gt;</c>), which doubles as ordinary code reuse.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
public interface ILocalizer<[TranslationScope] T> : ILocalizer
{
}
