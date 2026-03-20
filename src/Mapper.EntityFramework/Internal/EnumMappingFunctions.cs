using System.Reflection;

namespace ArchPillar.Extensions.Mapper.EntityFramework.Internal;

/// <summary>
/// Static marker method intercepted by the <see cref="EnumMapperMethodCallTranslator"/>.
/// Never executed at runtime — EF Core translates calls into flat SQL CASE expressions.
/// </summary>
internal static class EnumMappingFunctions
{
    /// <summary>
    /// Placeholder that the query interceptor substitutes for
    /// <c>EnumMapper&lt;TSource, TDest&gt;.Map(source)</c> calls.
    /// The <see cref="EnumMapperMethodCallTranslator"/> recognises this method
    /// and emits a flat <c>CaseExpression</c> with one <c>CaseWhenClause</c> per
    /// enum value.
    /// </summary>
    internal static TDest MapEnum<TSource, TDest>(TSource source)
        where TSource : struct, Enum
        where TDest : struct, Enum
    {
        throw new InvalidOperationException(
            $"EnumMappingFunctions.MapEnum<{typeof(TSource).Name}, {typeof(TDest).Name}>() " +
            "is an EF Core translation marker and must not be called directly. " +
            "Register the ArchPillar mapper integration with UseArchPillarMapper().");
    }

    /// <summary>
    /// Cached open-generic <see cref="MethodInfo"/> for <see cref="MapEnum{TSource,TDest}"/>.
    /// Used by the interceptor to build closed-generic <see cref="System.Linq.Expressions.MethodCallExpression"/> nodes
    /// and by the translator to recognise incoming calls.
    /// </summary>
    internal static readonly MethodInfo MapEnumMethod =
        typeof(EnumMappingFunctions)
            .GetMethod(nameof(MapEnum), BindingFlags.Static | BindingFlags.NonPublic)!;
}
