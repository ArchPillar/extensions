namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// An <see cref="ILocalizer"/> bound to a single category, forwarding to the root <see cref="Localizer"/>.
/// All instances share the root's loaded snapshot; only the category they look up under differs.
/// </summary>
internal class CategoryLocalizer(Localizer root, string category) : ILocalizer
{
    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments) =>
        root.TranslateInCategory(category, key, defaultMessage, context: null, arguments);

    /// <inheritdoc />
    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments) =>
        root.TranslateInCategory(category, key, defaultMessage, context, arguments);
}

/// <summary>
/// The typed category localizer: its category is the full name of <typeparamref name="T"/>, derived once.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
internal sealed class CategoryLocalizer<T>(Localizer root)
    : CategoryLocalizer(root, CategoryName.Of(typeof(T))), ILocalizer<T>;
