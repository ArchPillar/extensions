using System.Linq.Expressions;
using ArchPillar.Mapper.Internal;

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
    private readonly List<IncludeSet.IncludeEntry>  _includes         = [];
    private readonly Dictionary<object, object?>    _variableBindings = [];

    internal IReadOnlyList<IncludeSet.IncludeEntry> Includes         => _includes;
    internal IReadOnlyDictionary<object, object?>   VariableBindings => _variableBindings;

    /// <summary>
    /// Requests an optional scalar property declared with
    /// <c>IMapperBuilder.Optional()</c>.
    /// </summary>
    public ProjectionOptions<TDest> Include<TValue>(
        Expression<Func<TDest, TValue>> optionalProp)
    {
        _includes.Add(new IncludeSet.ScalarInclude(ExtractMemberName(optionalProp)));
        return this;
    }

    /// <summary>
    /// Requests an optional collection property and configures optional
    /// properties on its element type — mirrors EF Core's <c>ThenInclude</c>.
    /// </summary>
    public ProjectionOptions<TDest> Include<TElement>(
        Expression<Func<TDest, IEnumerable<TElement>>> collectionProp,
        Action<ProjectionOptions<TElement>> elementOptions)
    {
        var memberName = ExtractMemberName(collectionProp);
        var nested = new ProjectionOptions<TElement>();
        elementOptions(nested);
        _includes.Add(new IncludeSet.NestedInclude(memberName, nested.Includes));
        return this;
    }

    /// <summary>
    /// Requests one or more optional properties via a dot-separated string path.
    /// Validated at call time against the mapper's declared optionals.
    /// </summary>
    public ProjectionOptions<TDest> Include(string path)
    {
        _includes.Add(new IncludeSet.StringPathInclude(path));
        return this;
    }

    /// <summary>
    /// Binds a <see cref="Variable{T}"/> to a concrete value for this projection.
    /// Variables not bound here resolve to <c>default(T)</c>.
    /// </summary>
    public ProjectionOptions<TDest> Set<T>(Variable<T> variable, T value)
    {
        _variableBindings[variable] = value;
        return this;
    }

    private static string ExtractMemberName<TValue>(Expression<Func<TDest, TValue>> expression)
    {
        if (expression.Body is MemberExpression member)
            return member.Member.Name;
        throw new ArgumentException(
            $"Expression must be a simple property access, but got: {expression.Body.NodeType}.",
            nameof(expression));
    }
}
