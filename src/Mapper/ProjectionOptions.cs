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
    private readonly List<IncludeEntry>           _includes         = [];
    private readonly Dictionary<object, object?>  _variableBindings = [];

    internal IReadOnlyList<IncludeEntry>          Includes          => _includes;
    internal IReadOnlyDictionary<object, object?> VariableBindings  => _variableBindings;

    /// <summary>
    /// Requests an optional scalar property declared with
    /// <c>IMapperBuilder.Optional()</c>.
    /// </summary>
    public ProjectionOptions<TDest> Include<TValue>(
        Expression<Func<TDest, TValue>> optionalProp)
    {
        _includes.Add(new ScalarInclude(ExtractMemberName(optionalProp)));
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
        _includes.Add(new CollectionInclude(memberName, () =>
        {
            var nested = new ProjectionOptions<TElement>();
            elementOptions(nested);
            return nested;
        }));
        return this;
    }

    /// <summary>
    /// Requests one or more optional properties via a dot-separated string path.
    /// Validated at call time against the mapper's declared optionals.
    /// </summary>
    public ProjectionOptions<TDest> Include(string path)
    {
        _includes.Add(new StringPathInclude(path));
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

    // -------------------------------------------------------------------------
    // Internal include-entry types
    // -------------------------------------------------------------------------

    internal abstract class IncludeEntry { }

    internal sealed class ScalarInclude(string memberName) : IncludeEntry
    {
        public string MemberName { get; } = memberName;
    }

    internal sealed class StringPathInclude(string path) : IncludeEntry
    {
        public string Path { get; } = path;
    }

    /// <summary>
    /// A collection property include with a factory that creates and configures
    /// a nested <c>ProjectionOptions&lt;TElement&gt;</c> on demand.
    /// </summary>
    internal sealed class CollectionInclude(string memberName, Func<object> nestedOptionsFactory)
        : IncludeEntry
    {
        public string       MemberName            { get; } = memberName;
        public Func<object> NestedOptionsFactory  { get; } = nestedOptionsFactory;
    }
}
