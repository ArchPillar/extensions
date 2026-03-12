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
/// <para>
/// Instances are obtained from <see cref="MapperContext.CreateMapper{TSource,TDest}"/>.
/// Assigning the builder to a <see cref="Mapper{TSource,TDest}"/> property
/// triggers <see cref="Build"/> implicitly via the conversion operator.
/// </para>
/// </summary>
public sealed class MapperBuilder<TSource, TDest>
{
    private readonly Expression<Func<TSource, TDest>>? _memberInitExpression;
    private readonly List<PropertyMapping>             _mappings = [];

    internal MapperBuilder(Expression<Func<TSource, TDest>>? memberInitExpression)
    {
        _memberInitExpression = memberInitExpression;
    }

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
    /// <see cref="ProjectionOptions{TDest}.Include{TValue}(Expression{Func{TDest,TValue}})"/>
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
        // Build a dictionary of all mappings keyed by destination name.
        // Member-init bindings go first, then fluent calls overwrite naturally (last wins).
        var rawMappings = new Dictionary<string, PropertyMapping>(StringComparer.Ordinal);

        if (_memberInitExpression?.Body is MemberInitExpression init)
        {
            ParameterExpression sourceParam = _memberInitExpression.Parameters[0];
            foreach (MemberBinding binding in init.Bindings)
            {
                if (binding is MemberAssignment assignment)
                {
                    LambdaExpression lambda = Expression.Lambda(assignment.Expression, sourceParam);
                    rawMappings[binding.Member.Name] = new PropertyMapping(binding.Member, lambda, MappingKind.Required);
                }
            }
        }

        foreach (PropertyMapping mapping in _mappings)
        {
            rawMappings[mapping.Destination.Name] = mapping;
        }

        // Validate that every settable destination property is accounted for.
        var nullabilityCtx = new NullabilityInfoContext();
        List<string> uncovered =
        [
            .. from p in typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
               where p.CanWrite && RequiresCoverage(p, nullabilityCtx)
               where !rawMappings.ContainsKey(p.Name)
               select p.Name
        ];

        if (uncovered.Count > 0)
        {
            throw new InvalidOperationException(
                $"The following properties of {typeof(TDest).Name} are not mapped: " +
                string.Join(", ", uncovered));
        }

        // Remove ignored entries — they served their purpose for the coverage check.
        foreach (var key in rawMappings.Where(kv => kv.Value.Kind == MappingKind.Ignored).Select(kv => kv.Key).ToList())
        {
            rawMappings.Remove(key);
        }

        // For each raw mapping, detect nested Mapper<,> references (for cascade support).
        // Source expressions are stored raw (un-inlined); inlining is deferred to first
        // use so that nested mappers do not need to exist at build time.
        List<PropertyMapping> allMappings =
        [
            .. rawMappings.Values.Select(m =>
            {
                var detector = new NestedMapperDetector();
                detector.Detect(m.Source!.Body);

                if (!detector.Found)
                {
                    return m;
                }

                // Wrap the source-access expression in a lambda with the same parameter
                // as the mapping's Source lambda so BuildExpression can substitute it.
                LambdaExpression sourceAccess = Expression.Lambda(detector.SourceAccess!, m.Source.Parameters[0]);

                return m with
                {
                    NestedMapperAccessor = detector.NestedMapperAccessor,
                    NestedSourceAccess   = sourceAccess,
                    IsCollection         = detector.IsCollection,
                };
            })
        ];

        return new Mapper<TSource, TDest>(allMappings);
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
        {
            return member.Member;
        }

        throw new ArgumentException(
            $"Expression must be a simple property access (e.g. dest => dest.PropertyName), " +
            $"but got: {expression.Body.NodeType}.",
            nameof(expression));
    }

    /// <summary>
    /// Returns <see langword="true"/> when the property must be explicitly covered by
    /// a Map / Optional / Ignore call. Nullable non-required reference-type properties
    /// (and nullable value types) are auto-ignored — they receive their null default
    /// when left unmapped.
    /// </summary>
    private static bool RequiresCoverage(PropertyInfo p, NullabilityInfoContext ctx)
    {
        // Non-nullable value type (int, decimal, …) → always required
        if (p.PropertyType.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) == null)
        {
            return true;
        }

        // Nullable value type (int?, decimal?, …) → auto-ignored
        if (p.PropertyType.IsValueType)
        {
            return false;
        }

        // Reference type: required only when NRT annotation says non-nullable
        NullabilityInfo info = ctx.Create(p);
        return info.WriteState != NullabilityState.Nullable;
    }
}
