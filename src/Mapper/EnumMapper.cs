using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// A mapping between two enum types, created via
/// <see cref="MapperContext.CreateEnumMapper{TSource,TDest}"/>.
/// <para>
/// The mapping is defined as a plain C# method (typically a switch expression).
/// The library generates a LINQ-translatable conditional expression tree by
/// calling the method for every possible <typeparamref name="TSource"/> value
/// and recording the output.
/// </para>
/// <para>
/// Can be used standalone or inlined into a parent mapper's expression:
/// </para>
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Status = OrderStatus.Map(src.Status),
/// });
/// </code>
/// </summary>
/// <typeparam name="TSource">The source enum type.</typeparam>
/// <typeparam name="TDest">The destination enum type.</typeparam>
public sealed class EnumMapper<TSource, TDest>(Func<TSource, TDest> mappingMethod)
    : IMapper
    where TSource : struct, Enum
    where TDest   : struct, Enum
{
    private readonly Lazy<Expression<Func<TSource, TDest>>> _expression =
        new(() => BuildExpression(mappingMethod));

    /// <summary>
    /// Maps a single source enum value to the destination enum type.
    /// </summary>
    public TDest Map(TSource source)
        => mappingMethod(source);

    /// <summary>
    /// Returns the mapping as a LINQ expression tree (a conditional chain
    /// covering every <typeparamref name="TSource"/> value).
    /// </summary>
    public Expression<Func<TSource, TDest>> ToExpression()
        => _expression.Value;

    LambdaExpression IMapper.GetRawExpression(IncludeSet includes, int depth)
    {
        return ToExpression();
    }

    void IMapper.Compile()
    {
        _ = ToExpression();
    }

    private static Expression<Func<TSource, TDest>> BuildExpression(Func<TSource, TDest> method)
    {
        ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "source");
        TSource[] values = Enum.GetValues<TSource>();

        // Cast the source enum to its underlying integer type so that LINQ
        // providers (EF Core, etc.) can translate the equality checks to SQL.
        // Bare enum comparisons are not translatable by most providers.
        Type sourceUnderlying = Enum.GetUnderlyingType(typeof(TSource));
        Type destUnderlying = Enum.GetUnderlyingType(typeof(TDest));
        Expression sourceAsInt = Expression.Convert(sourceParam, sourceUnderlying);

        // Pre-compute the (sourceInt, destInt) pairs.  Both sides use their
        // underlying integer types so the conditional tree contains only plain
        // integer constants that every SQL provider can translate.
        var entries = new (object sourceInt, object destInt)[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            TSource value = values[i];
            entries[i] = (
                Convert.ChangeType(value, sourceUnderlying),
                Convert.ChangeType(method(value), destUnderlying));
        }

        // Build a balanced binary tree of ConditionalExpressions.  EF Core does
        // NOT flatten nested ternaries into a single SQL CASE — each level
        // becomes a separate CASE WHEN … ELSE (CASE WHEN …).  A linear chain
        // of N conditionals produces N nested CASEs, which exceeds the 10-level
        // nesting limit on SQL Server and fails translation on other providers
        // once the enum has enough members.  A balanced tree keeps the depth at
        // O(log₂ N) — e.g. ≈ 4 for 11 values, ≈ 7 for 100.
        Expression body = BuildBalancedTree(
            sourceAsInt, entries, 0, entries.Length - 1,
            sourceUnderlying, destUnderlying);

        // Single outer Convert from the underlying integer type to TDest.
        // EF Core stores enums as their underlying integer type by default,
        // so this translates to a no-op (or trivial implicit cast) in SQL.
        body = Expression.Convert(body, typeof(TDest));

        return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam);
    }

    /// <summary>
    /// Recursively builds a balanced binary search tree of
    /// <see cref="ConditionalExpression"/> nodes over the given
    /// <paramref name="entries"/> slice [<paramref name="lo"/> ..
    /// <paramref name="hi"/>].  Intermediate nodes split on ≤ comparisons;
    /// leaf nodes (1–2 entries) use direct equality checks.
    /// </summary>
    private static Expression BuildBalancedTree(
        Expression sourceAsInt,
        (object sourceInt, object destInt)[] entries,
        int lo,
        int hi,
        Type sourceUnderlying,
        Type destUnderlying)
    {
        if (lo == hi)
        {
            // Single entry — the binary search has narrowed to this value.
            return Expression.Constant(entries[lo].destInt, destUnderlying);
        }

        if (lo + 1 == hi)
        {
            // Two entries — one equality check decides between them.
            return Expression.Condition(
                Expression.Equal(
                    sourceAsInt,
                    Expression.Constant(entries[lo].sourceInt, sourceUnderlying)),
                Expression.Constant(entries[lo].destInt, destUnderlying),
                Expression.Constant(entries[hi].destInt, destUnderlying));
        }

        // Three or more entries — split at the midpoint.
        var mid = (lo + hi) / 2;

        return Expression.Condition(
            Expression.LessThanOrEqual(
                sourceAsInt,
                Expression.Constant(entries[mid].sourceInt, sourceUnderlying)),
            BuildBalancedTree(sourceAsInt, entries, lo, mid, sourceUnderlying, destUnderlying),
            BuildBalancedTree(sourceAsInt, entries, mid + 1, hi, sourceUnderlying, destUnderlying));
    }
}
