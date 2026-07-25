namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Computes the translation category for a scope type — the value that scopes an <see cref="ILocalizer{T}"/>'s keys,
/// exposed so a custom adapter (or any caller) can derive the same category the runtime and the build-time extractor
/// use. It is the type's full name, except a generic type uses its open-generic form (the arity backtick without the
/// type arguments, for example <c>Acme.Box`1</c>) — <see cref="System.Type.FullName"/> for a closed generic includes
/// the assembly-qualified type arguments and would never match the extracted category.
/// </summary>
public static class CategoryName
{
    /// <summary>
    /// Returns the translation category for <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The scope type.</param>
    /// <returns>The type's full name, or its open-generic name (<c>Acme.Box`1</c>) for a generic type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return definition.FullName ?? definition.Name;
    }
}
