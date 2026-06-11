using System.Globalization;

namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// The global-namespace ambient localizer behind <see cref="Localizer.Default"/>: every call resolves
/// against the latest process-wide store for the current UI culture, falling back to the in-code default.
/// </summary>
internal sealed class AmbientLocalizer : ILocalizer
{
    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        Localizer.EnsureCulture(culture);
        return Localizer.Current.Translate(culture, key, defaultMessage, context: null, arguments);
    }

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        Localizer.EnsureCulture(culture);
        return Localizer.Current.Translate(culture, key, defaultMessage, context, arguments);
    }
}

/// <summary>
/// The category-scoped ambient localizer behind <see cref="Localizer.For{T}"/>: its category is the full
/// name of <typeparamref name="T"/>, resolved once, against the same process-wide store.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
internal sealed class AmbientCategoryLocalizer<T> : ILocalizer<T>
{
    private static readonly string _category = CategoryName.Of(typeof(T));

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments)
    {
        Localizer.EnsureCulture(CultureInfo.CurrentUICulture);
        return Localizer.Current.TranslateInCategory(_category, key, defaultMessage, context: null, arguments);
    }

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments)
    {
        Localizer.EnsureCulture(CultureInfo.CurrentUICulture);
        return Localizer.Current.TranslateInCategory(_category, key, defaultMessage, context, arguments);
    }
}
