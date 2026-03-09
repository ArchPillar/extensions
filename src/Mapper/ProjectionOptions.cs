using System.Linq.Expressions;

namespace ArchPillar.Mapper;

/// <summary>
/// Configures a LINQ projection produced by
/// <see cref="MapperQueryableExtensions.Project{TSource,TDest}"/> or
/// <see cref="Mapper{TSource,TDest}.ToExpression"/>.
/// Extends <see cref="MapOptions{TDest}"/> with the ability to bind
/// <see cref="Variable{T}"/> values.
/// </summary>
/// <typeparam name="TDest">The destination type whose optional properties and variables are being configured.</typeparam>
public sealed class ProjectionOptions<TDest>
{
    /// <summary>
    /// Requests an optional scalar property declared with
    /// <c>IMapperBuilder.Optional()</c>.
    /// </summary>
    public ProjectionOptions<TDest> Include<TValue>(
        Expression<Func<TDest, TValue>> optionalProp)
        => throw new NotImplementedException();

    /// <summary>
    /// Requests an optional collection property and configures optional
    /// properties on its element type — mirrors EF Core's <c>ThenInclude</c>.
    /// </summary>
    public ProjectionOptions<TDest> Include<TElement>(
        Expression<Func<TDest, IEnumerable<TElement>>> collectionProp,
        Action<ProjectionOptions<TElement>> elementOptions)
        => throw new NotImplementedException();

    /// <summary>
    /// Requests one or more optional properties via a dot-separated string path.
    /// Validated at call time against the mapper's declared optionals.
    /// </summary>
    public ProjectionOptions<TDest> Include(string path)
        => throw new NotImplementedException();

    /// <summary>
    /// Binds a <see cref="Variable{T}"/> to a concrete value for this projection.
    /// Variables not bound here resolve to <c>default(T)</c>.
    /// </summary>
    public ProjectionOptions<TDest> Set<T>(Variable<T> variable, T value)
        => throw new NotImplementedException();
}
