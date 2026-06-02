using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore;

/// <summary>
/// EF Core-aware <c>Map</c> and <c>Project</c> overloads that accept
/// <see cref="ProjectionOptions{TDest}"/>, for use inside a hand-written LINQ
/// query projection where only some properties are produced by a mapper.
/// <para>
/// When <see cref="MapperDbContextOptionsExtensions.UseArchPillarMapper"/> is
/// registered, calls to these methods inside an <see cref="IQueryable{T}"/>
/// query are inlined into the mapper's projection expression at query-compilation
/// time, so the whole projection is translated server-side. Outside a query they
/// fall back to compiling and invoking the mapper's projection expression.
/// </para>
/// </summary>
public static class MapperEfCoreExtensions
{
    /// <summary>
    /// Projects a single <paramref name="source"/> using <paramref name="mapper"/>
    /// with the supplied projection <paramref name="configure"/> (optional
    /// properties and variable bindings), for use inside a query projection.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="mapper">The mapper to project with.</param>
    /// <param name="source">The source instance to project.</param>
    /// <param name="configure">Configures optional properties and variables for the projection.</param>
    /// <returns>The mapped destination, or <see langword="null"/> when <paramref name="source"/> is <see langword="null"/>.</returns>
    public static TDest? Map<TSource, TDest>(
        this Mapper<TSource, TDest> mapper,
        TSource? source,
        Action<ProjectionOptions<TDest>> configure)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        if (source is null)
        {
            return default;
        }

        Func<TSource, TDest> compiled = mapper.ToExpression(configure).Compile();
        return compiled(source);
    }

    /// <summary>
    /// Projects each element of <paramref name="source"/> using
    /// <paramref name="mapper"/> with the supplied projection
    /// <paramref name="configure"/>, for use inside a query projection.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TDest">The destination element type.</typeparam>
    /// <param name="source">The source sequence to project.</param>
    /// <param name="mapper">The mapper to project each element with.</param>
    /// <param name="configure">Configures optional properties and variables for the projection.</param>
    /// <returns>A projected sequence; materialize it with <c>ToList()</c>, <c>ToArray()</c>, etc.</returns>
    public static IEnumerable<TDest> Project<TSource, TDest>(
        this IEnumerable<TSource> source,
        Mapper<TSource, TDest> mapper,
        Action<ProjectionOptions<TDest>> configure)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        Func<TSource, TDest> compiled = mapper.ToExpression(configure).Compile();
        return source.Select(compiled);
    }

    /// <summary>
    /// Inlines every direct <c>mapper.Map(...)</c> and <c>source.Project(mapper)</c>
    /// call inside a hand-written LINQ query into the mapper's projection
    /// expression, at query-construction time.
    /// <para>
    /// This is the explicit, opt-in counterpart to the automatic inlining performed
    /// by <see cref="MapperDbContextOptionsExtensions.UseArchPillarMapper"/>. Because
    /// it runs <em>before</em> EF Core's parameter extraction, it supports mappers
    /// that contain <see cref="Mapper{TSource,TDest}.Invoke(TSource)"/> calls (the
    /// nested mapping runs in memory on the materialised source) — which the
    /// automatic interceptor cannot, since it runs after parameter extraction.
    /// </para>
    /// <para>
    /// Use it on a query whose projection calls a mapper that contains <c>Invoke</c>:
    /// <code>
    /// var rows = await db.Orders
    ///     .Select(o => new Row { Id = o.Id, Dto = mappers.Order.Map(o)! })
    ///     .InlineMappers()
    ///     .ToListAsync();
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="TSource">The query element type.</typeparam>
    /// <param name="query">The query whose mapper calls should be inlined.</param>
    /// <returns>An equivalent query with mapper calls inlined into the projection.</returns>
    public static IQueryable<TSource> InlineMappers<TSource>(this IQueryable<TSource> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Expression rewritten = new MapperCallRewriter(flattenVariableBoxes: false).Visit(query.Expression)!;
        return query.Provider.CreateQuery<TSource>(rewritten);
    }
}
