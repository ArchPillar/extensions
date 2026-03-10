using System.Linq.Expressions;

namespace ArchPillar.Mapper;

/// <summary>
/// Configures optional properties for an in-memory <see cref="Mapper{TSource,TDest}.Map"/> call.
/// </summary>
/// <typeparam name="TDest">The destination type whose optional properties are being requested.</typeparam>
public sealed class MapOptions<TDest>
{
    private readonly List<IncludeEntry>           _includes         = [];
    private readonly Dictionary<object, object?>  _variableBindings = [];

    internal IReadOnlyList<IncludeEntry>          Includes          => _includes;
    internal IReadOnlyDictionary<object, object?> VariableBindings  => _variableBindings;

    /// <summary>
    /// Requests an optional scalar property declared with
    /// <c>IMapperBuilder.Optional()</c>.
    /// </summary>
    public MapOptions<TDest> Include<TValue>(
        Expression<Func<TDest, TValue>> optionalProp)
    {
        _includes.Add(new ScalarInclude(ExtractMemberName(optionalProp)));
        return this;
    }

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
    {
        var memberName = ExtractMemberName(collectionProp);
        _includes.Add(new CollectionInclude(memberName, () =>
        {
            var nested = new MapOptions<TElement>();
            elementOptions(nested);
            return nested;
        }));
        return this;
    }

    /// <summary>
    /// Requests one or more optional properties using a dot-separated path
    /// (e.g. <c>"CustomerName"</c> or <c>"Lines.SupplierName"</c>).
    /// Useful when the set of includes is driven by an HTTP API parameter or
    /// other external input. Validated at call time; an unknown path throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public MapOptions<TDest> Include(string path)
    {
        _includes.Add(new StringPathInclude(path));
        return this;
    }

    /// <summary>
    /// Binds a <see cref="Variable{T}"/> to a concrete value for this in-memory mapping call.
    /// Variables not bound here resolve to <c>default(T)</c>.
    /// </summary>
    public MapOptions<TDest> Set<T>(Variable<T> variable, T value)
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
    /// a nested <c>MapOptions&lt;TElement&gt;</c> on demand.
    /// </summary>
    internal sealed class CollectionInclude(string memberName, Func<object> nestedOptionsFactory)
        : IncludeEntry
    {
        public string       MemberName            { get; } = memberName;
        public Func<object> NestedOptionsFactory  { get; } = nestedOptionsFactory;
    }
}
