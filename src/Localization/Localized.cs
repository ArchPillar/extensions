using System.Runtime.CompilerServices;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// An optional base class for a self-scoped set of localized strings. A type that derives from
/// <c>Localized&lt;Self&gt;</c> exposes one member per string and calls <see cref="Translate(string, string)"/>
/// with only the source default: the calling member's name becomes the key (via
/// <see cref="CallerMemberNameAttribute"/>) and the deriving type is the category, so neither has to be
/// repeated. The localizer is supplied through the constructor, which the deriving type threads up:
/// <code>
/// public sealed class ButtonLabels(ILocalizer&lt;ButtonLabels&gt; loc) : Localized&lt;ButtonLabels&gt;(loc)
/// {
///     public string Save => Translate("Save");
/// }
/// </code>
/// </summary>
/// <typeparam name="TSelf">The deriving type; its full name is the translation category.</typeparam>
public abstract class Localized<[TranslationScope] TSelf>(ILocalizer<TSelf> localizer)
    where TSelf : Localized<TSelf>
{
    /// <summary>
    /// Constructs a bundle bound to the process-wide ambient context, resolving its localizer from
    /// <see cref="Localizer.For{T}"/>. This is the ambient/no-DI path: a single <c>new TodoStrings()</c>
    /// needs no services and no registration, which suits small console apps and scripts. Throws if the
    /// ambient context has been disabled (<c>UseAmbient = false</c>); in that static-free configuration,
    /// inject an <see cref="ILocalizer{TSelf}"/> through the other constructor instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ambient localization context is disabled.</exception>
    protected Localized()
        : this(Localizer.For<TSelf>())
    {
    }

    /// <summary>
    /// Translates the calling member's string, taking the member name as the key.
    /// </summary>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="key">The translation key; defaults to the calling member's name and should not be passed explicitly.</param>
    /// <returns>The rendered string.</returns>
    protected string Translate(
        [TranslationDefault] string defaultMessage,
        [Translatable, CallerMemberName] string key = "")
    {
        return localizer.Translate(key, defaultMessage);
    }

    /// <summary>
    /// Translates the calling member's string with ICU <paramref name="arguments"/>, taking the member
    /// name as the key.
    /// </summary>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <param name="key">The translation key; defaults to the calling member's name and should not be passed explicitly.</param>
    /// <returns>The rendered string.</returns>
    protected string Translate(
        [TranslationDefault] string defaultMessage,
        (string Name, object? Value)[] arguments,
        [Translatable, CallerMemberName] string key = "")
    {
        return localizer.Translate(key, defaultMessage, arguments);
    }
}
