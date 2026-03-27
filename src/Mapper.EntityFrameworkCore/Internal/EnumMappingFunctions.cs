using System.Reflection;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

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
    public static TDest MapEnum<TSource, TDest>(TSource source)
        where TSource : struct, Enum
        where TDest : struct, Enum
    {
        throw new InvalidOperationException(
            $"EnumMappingFunctions.MapEnum<{typeof(TSource).Name}, {typeof(TDest).Name}>() " +
            "is an EF Core translation marker and must not be called directly. " +
            "Register the ArchPillar mapper integration with UseArchPillarMapper().");
    }

    /// <summary>
    /// Placeholder that the query interceptor substitutes for
    /// <c>EnumMapper&lt;TSource, TDest&gt;.Map(TSource? source)</c> calls where
    /// the source is nullable. The translator wraps the flat CASE expression in
    /// a null guard: <c>CASE WHEN source IS NOT NULL THEN … ELSE NULL END</c>.
    /// </summary>
    public static TDest? MapEnumNullable<TSource, TDest>(TSource? source)
        where TSource : struct, Enum
        where TDest : struct, Enum
    {
        throw new InvalidOperationException(
            $"EnumMappingFunctions.MapEnumNullable<{typeof(TSource).Name}, {typeof(TDest).Name}>() " +
            "is an EF Core translation marker and must not be called directly. " +
            "Register the ArchPillar mapper integration with UseArchPillarMapper().");
    }

    /// <summary>
    /// Placeholder that the query interceptor substitutes for
    /// <c>EnumMapper&lt;TSource, TDest&gt;.Map(TSource? source, TDest defaultValue)</c>
    /// calls. The translator wraps the flat CASE expression with a null guard
    /// that falls back to <paramref name="defaultValue"/>:
    /// <c>CASE WHEN source IS NOT NULL THEN … ELSE defaultValue END</c>.
    /// </summary>
    public static TDest MapEnumNullableWithDefault<TSource, TDest>(TSource? source, TDest defaultValue)
        where TSource : struct, Enum
        where TDest : struct, Enum
    {
        throw new InvalidOperationException(
            $"EnumMappingFunctions.MapEnumNullableWithDefault<{typeof(TSource).Name}, {typeof(TDest).Name}>() " +
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
            .GetMethod(nameof(MapEnum), BindingFlags.Static | BindingFlags.Public)!;

    /// <summary>
    /// Cached open-generic <see cref="MethodInfo"/> for <see cref="MapEnumNullable{TSource,TDest}"/>.
    /// </summary>
    internal static readonly MethodInfo MapEnumNullableMethod =
        typeof(EnumMappingFunctions)
            .GetMethod(nameof(MapEnumNullable), BindingFlags.Static | BindingFlags.Public)!;

    /// <summary>
    /// Cached open-generic <see cref="MethodInfo"/> for <see cref="MapEnumNullableWithDefault{TSource,TDest}"/>.
    /// </summary>
    internal static readonly MethodInfo MapEnumNullableWithDefaultMethod =
        typeof(EnumMappingFunctions)
            .GetMethod(nameof(MapEnumNullableWithDefault), BindingFlags.Static | BindingFlags.Public)!;
}
