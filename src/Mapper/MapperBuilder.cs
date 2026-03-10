using System.Linq.Expressions;
using System.Reflection;
using ArchPillar.Mapper.Internal;

namespace ArchPillar.Mapper;

/// <summary>
/// Fluent builder for configuring a <see cref="Mapper{TSource,TDest}"/>.
/// Every destination property must be covered by exactly one of
/// <see cref="Map{TValue}"/>, <see cref="Optional{TValue}"/>, or
/// <see cref="Ignore{TValue}"/>; any unaccounted property causes an
/// <see cref="InvalidOperationException"/> when the mapper is built.
///
/// Instances are obtained from <see cref="MapperContext.CreateMapper{TSource,TDest}"/>.
/// Assigning the builder to a <see cref="Mapper{TSource,TDest}"/> property
/// triggers <see cref="Build"/> implicitly via the conversion operator.
/// </summary>
public sealed class MapperBuilder<TSource, TDest>
{
    private readonly Expression<Func<TSource, TDest>>? _memberInitExpression;
    private readonly List<PropertyMapping>             _mappings = [];

    internal MapperBuilder(Expression<Func<TSource, TDest>>? memberInitExpression)
        => _memberInitExpression = memberInitExpression;

    /// <summary>
    /// Maps a destination property from a source expression.
    /// Included in both in-memory mapping and LINQ projection.
    /// </summary>
    public MapperBuilder<TSource, TDest> Map<TValue>(
        Expression<Func<TDest,   TValue>> destination,
        Expression<Func<TSource, TValue>> source)
    {
        _mappings.Add(new PropertyMapping(ExtractMember(destination), source, MappingKind.Required));
        return this;
    }

    /// <summary>
    /// Declares an opt-in property excluded from the default mapping.
    /// Must be explicitly requested at the call site via
    /// <see cref="MapOptions{TDest}.Include{TValue}(Expression{Func{TDest,TValue}})"/>
    /// or the string-path overload.
    /// </summary>
    public MapperBuilder<TSource, TDest> Optional<TValue>(
        Expression<Func<TDest,   TValue>> destination,
        Expression<Func<TSource, TValue>> source)
    {
        _mappings.Add(new PropertyMapping(ExtractMember(destination), source, MappingKind.Optional));
        return this;
    }

    /// <summary>
    /// Marks a destination property as intentionally unmapped.
    /// Required for any property not covered by <see cref="Map{TValue}"/>
    /// or <see cref="Optional{TValue}"/> so the builder can verify full coverage.
    /// </summary>
    public MapperBuilder<TSource, TDest> Ignore<TValue>(
        Expression<Func<TDest, TValue>> destination)
    {
        _mappings.Add(new PropertyMapping(ExtractMember(destination), null, MappingKind.Ignored));
        return this;
    }

    /// <summary>
    /// Finalizes configuration and returns the built mapper.
    /// Throws <see cref="InvalidOperationException"/> if any destination
    /// property is unaccounted for.
    /// </summary>
    public Mapper<TSource, TDest> Build()
    {
        var settableProperties = typeof(TDest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        // Collect all covered property names.
        var coveredNames = new HashSet<string>(StringComparer.Ordinal);

        if (_memberInitExpression?.Body is MemberInitExpression memberInit)
            foreach (var binding in memberInit.Bindings)
                coveredNames.Add(binding.Member.Name);

        foreach (var mapping in _mappings)
            coveredNames.Add(mapping.Destination.Name);

        var uncovered = settableProperties
            .Where(p => !coveredNames.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();

        if (uncovered.Count > 0)
            throw new InvalidOperationException(
                $"The following properties of {typeof(TDest).Name} are not mapped: " +
                string.Join(", ", uncovered));

        // Normalize: extract bindings from the member-init expression, then append
        // the explicit fluent-call entries (Ignored entries are dropped here).
        var allMappings = new List<PropertyMapping>();

        if (_memberInitExpression?.Body is MemberInitExpression init)
        {
            var sourceParam = _memberInitExpression.Parameters[0];
            foreach (var binding in init.Bindings)
            {
                if (binding is MemberAssignment assignment)
                {
                    var lambda = Expression.Lambda(assignment.Expression, sourceParam);
                    allMappings.Add(new PropertyMapping(binding.Member, lambda, MappingKind.Required));
                }
            }
        }

        foreach (var mapping in _mappings)
            if (mapping.Kind != MappingKind.Ignored)
                allMappings.Add(mapping);

        // Inline any nested mapper and enum mapper calls in each source expression.
        var inliner = new NestedMapperInliner();
        var inlinedMappings = allMappings
            .Select(m => m with { Source = (LambdaExpression)inliner.Visit(m.Source!) })
            .ToList();

        return new Mapper<TSource, TDest>(inlinedMappings);
    }

    /// <summary>
    /// Allows assigning a builder directly to a <see cref="Mapper{TSource,TDest}"/>
    /// property without an explicit <see cref="Build"/> call.
    /// </summary>
    public static implicit operator Mapper<TSource, TDest>(MapperBuilder<TSource, TDest> builder)
        => builder.Build();

    private static MemberInfo ExtractMember<TValue>(Expression<Func<TDest, TValue>> expression)
    {
        if (expression.Body is MemberExpression member)
            return member.Member;

        throw new ArgumentException(
            $"Expression must be a simple property access (e.g. dest => dest.PropertyName), " +
            $"but got: {expression.Body.NodeType}.",
            nameof(expression));
    }
}
