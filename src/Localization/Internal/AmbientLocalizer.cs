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
/// The category-scoped localizer behind <see cref="LocalizationContext.For{T}"/>: its category is the full
/// name of <typeparamref name="T"/>, resolved once, against the same environment.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
/// <param name="environment">The owning localization environment.</param>
internal sealed class AmbientCategoryLocalizer<T>(LocalizationContext environment) : ILocalizer<T>
{
    private static readonly string _category = CategoryName.Of(typeof(T));

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments)
    {
        environment.EnsureCulture(CultureInfo.CurrentUICulture);
        return environment.Engine.TranslateInCategory(_category, key, defaultMessage, context: null, arguments);
    }

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments)
    {
        environment.EnsureCulture(CultureInfo.CurrentUICulture);
        return environment.Engine.TranslateInCategory(_category, key, defaultMessage, context, arguments);
    }
}
