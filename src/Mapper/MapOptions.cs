using System.Linq.Expressions;

namespace ArchPillar.Mapper;

/// <summary>
/// Configures optional properties for an in-memory <see cref="Mapper{TSource,TDest}.Map"/> call.
/// </summary>
/// <typeparam name="TDest">The destination type whose optional properties are being requested.</typeparam>
public sealed class MapOptions<TDest>
{
    /// <summary>
    /// Requests an optional scalar property declared with
    /// <c>IMapperBuilder.Optional()</c>.
    /// </summary>
    public MapOptions<TDest> Include<TValue>(
        Expression<Func<TDest, TValue>> optionalProp)
        => throw new NotImplementedException();

    /// <summary>
    /// Requests an optional collection property and configures optional
    /// properties on its element type via a nested callback — mirrors
    /// EF Core's <c>ThenInclude</c> pattern.
    /// </summary>
    /// <typeparam name="TElement">The collection element type.</typeparam>
    /// <param name="collectionProp">Selects the collection property on <typeparamref name="TDest"/>.</param>
    /// <param name="elementOptions">Configures optional properties on each element.</param>
    public MapOptions<TDest> Include<TElement>(
        Expression<Func<TDest, IEnumerable<TElement>>> collectionProp,
        Action<MapOptions<TElement>> elementOptions)
        => throw new NotImplementedException();

    /// <summary>
    /// Requests one or more optional properties using a dot-separated path
    /// (e.g. <c>"CustomerName"</c> or <c>"Lines.SupplierName"</c>).
    /// Useful when the set of includes is driven by an HTTP API parameter or
    /// other external input. Validated at call time; an unknown path throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public MapOptions<TDest> Include(string path)
        => throw new NotImplementedException();

    /// <summary>
    /// Binds a <see cref="Variable{T}"/> to a concrete value for this in-memory mapping call.
    /// Variables not bound here resolve to <c>default(T)</c>.
    /// </summary>
    public MapOptions<TDest> Set<T>(Variable<T> variable, T value)
        => throw new NotImplementedException();
}
