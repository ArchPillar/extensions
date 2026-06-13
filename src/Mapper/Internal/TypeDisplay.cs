using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Reflection helpers shared by the builder and the compiled mapper for
/// producing human-readable type names in diagnostics and for resolving the
/// value type of a mapped <see cref="MemberInfo"/>.
/// </summary>
internal static class TypeDisplay
{
    /// <summary>
    /// Produces a readable type name for diagnostics — e.g. <c>List&lt;OrderDto&gt;</c>
    /// or <c>OrderStatus?</c> — instead of the raw CLR name (<c>List`1</c>).
    /// </summary>
    public static string Describe(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Describe(underlying) + "?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(Describe));
        return $"{name}<{args}>";
    }

    /// <summary>
    /// Returns the value type of a mapped member — the property type for a
    /// <see cref="PropertyInfo"/> or the field type for a <see cref="FieldInfo"/>.
    /// </summary>
    public static Type MemberType(MemberInfo member)
        => member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field       => field.FieldType,
            _                     => typeof(object),
        };
}
