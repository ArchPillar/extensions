namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// LINQ extension methods for applying a <see cref="Mapper{TSource,TDest}"/>
/// as a projection on <see cref="IQueryable{T}"/> and <see cref="IEnumerable{T}"/> sequences.
/// </summary>
public static class MapperExtensions
{
    // -------------------------------------------------------------------------
    // IQueryable<T> — server-side projection (EF Core, etc.)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the mapper as a <c>Select</c> projection on the queryable.
    /// The mapper's expression tree (including inlined nested mappers,
    /// optional properties, and variable substitutions) is handed directly
    /// to the LINQ provider, enabling full server-side translation in EF Core.
    /// <code>
    /// var dtos = await dbContext.Orders
    ///     .Where(o => o.IsActive)
    ///     .Project(mapper.Order, o => o
    ///         .Include(m => m.CustomerName)
    ///         .Include("Lines.SupplierName")
    ///         .Set(mapper.CurrentUserId, currentUser.Id))
    ///     .ToListAsync();
    /// </code>
    /// </summary>
    public static IQueryable<TDest> Project<TSource, TDest>(
        this IQueryable<TSource> query,
        Mapper<TSource, TDest> mapper,
        Action<ProjectionOptions<TDest>>? options = null)
        where TDest : new()
    {
        return query.Select(mapper.ToExpression(options));
    }

    // -------------------------------------------------------------------------
    // IEnumerable<T> — in-memory projection and expression-tree inlining
    // -------------------------------------------------------------------------

    /// <summary>
    /// Projects each element of a sequence using the mapper.
    /// Returns <see cref="IEnumerable{TDest}"/> so the caller can choose the
    /// target collection type (<c>.ToList()</c>, <c>.ToArray()</c>,
    /// <c>.ToHashSet()</c>, etc.).
    /// <para>
    /// This overload has no optional parameters and is safe to use inside
    /// parent mapper member-init expressions — the expression visitor detects
    /// the call and inlines the mapper's expression as a <c>Select</c>:
    /// </para>
    /// <code>
    /// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
    /// {
    ///     Lines = src.Lines.Project(OrderLine).ToList(),
    ///     Tags  = src.Tags.Project(TagMapper).ToHashSet(),
    /// });
    /// </code>
    /// </summary>
    public static IEnumerable<TDest> Project<TSource, TDest>(
        this IEnumerable<TSource> source,
        Mapper<TSource, TDest> mapper)
        where TDest : new()
    {
        return source.Select(s => mapper.Map(s)!);
    }

    /// <summary>
    /// Projects each element of a sequence using the mapper, with optional-property
    /// and variable configuration applied per element.
    /// Not for use inside expression trees; use the no-options overload there.
    /// </summary>
    public static IEnumerable<TDest> Project<TSource, TDest>(
        this IEnumerable<TSource> source,
        Mapper<TSource, TDest> mapper,
        Action<MapOptions<TDest>> options)
        where TDest : new()
    {
        return source.Select(s => mapper.Map(s, options)!);
    }
}
