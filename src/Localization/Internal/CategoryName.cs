namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// Computes the translation category for a scope type. This must agree byte-for-byte with the extractor's
/// category (the Roslyn detector), so a generic scope type uses the open-generic form — the full name with
/// the arity backtick but without the type arguments (for example <c>Acme.Box`1</c>) — rather than
/// <see cref="System.Type.FullName"/>, which for a closed generic includes the assembly-qualified type
/// arguments and would never match.
/// </summary>
internal static class CategoryName
{
    public static string Of(Type type)
    {
        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return definition.FullName ?? definition.Name;
    }
}
