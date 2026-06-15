using System.Globalization;

namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// The global-namespace localizer behind <see cref="LocalizationContext.Default"/>: every call resolves
/// against its environment's store for the current UI culture, falling back to the in-code default.
/// </summary>
/// <param name="environment">The owning localization environment.</param>
internal sealed class AmbientLocalizer(LocalizationContext environment) : ILocalizer
{
    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        environment.EnsureCulture(culture);
        return environment.Engine.Translate(culture, key, defaultMessage, context: null, arguments);
    }

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        environment.EnsureCulture(culture);
        return environment.Engine.Translate(culture, key, defaultMessage, context, arguments);
    }
}

/// <summary>
/// The category-scoped localizer behind <see cref="LocalizationContext.ForCategory(string)"/>: its category is
/// a string computed at runtime, resolved against the same environment. The base of the typed
/// <see cref="AmbientCategoryLocalizer{T}"/>.
/// </summary>
/// <param name="environment">The owning localization environment.</param>
/// <param name="category">The translation category to resolve under.</param>
internal class AmbientCategoryLocalizer(LocalizationContext environment, string category) : ILocalizer
{
    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments)
    {
        environment.EnsureCulture(CultureInfo.CurrentUICulture);
        return environment.Engine.TranslateInCategory(category, key, defaultMessage, context: null, arguments);
    }

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments)
    {
        environment.EnsureCulture(CultureInfo.CurrentUICulture);
        return environment.Engine.TranslateInCategory(category, key, defaultMessage, context, arguments);
    }
}

/// <summary>
/// The typed category localizer behind <see cref="LocalizationContext.For{T}"/>: its category is the full name
/// of <typeparamref name="T"/>, against the same environment.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
/// <param name="environment">The owning localization environment.</param>
internal sealed class AmbientCategoryLocalizer<T>(LocalizationContext environment)
    : AmbientCategoryLocalizer(environment, CategoryName.Of(typeof(T))), ILocalizer<T>;
