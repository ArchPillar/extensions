using System.Linq.Expressions;
using System.Reflection;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Fluent builder for configuring a <see cref="Mapper{TSource,TDest}"/>.
/// By default, destination properties must be covered by
/// <see cref="Map{TValue}"/>, <see cref="Optional{TValue}"/>, or
/// <see cref="Ignore{TValue}"/> according to the active
/// <see cref="CoverageValidation"/> mode. The mode is inherited from the
/// <see cref="MapperContext"/> and can be overridden per-mapper via
/// <see cref="SetCoverageValidation"/>.
/// <para>
/// Instances are obtained from <see cref="MapperContext.CreateMapper{TSource,TDest}"/>.
/// Assigning the builder to a <see cref="Mapper{TSource,TDest}"/> property
/// triggers <see cref="Build"/> implicitly via the conversion operator.
/// </para>
/// </summary>
public sealed class MapperBuilder<TSource, TDest>
{
    private readonly Expression<Func<TSource, TDest>>?    _memberInitExpression;
    private readonly List<PropertyMapping>                _mappings = [];
    private readonly IReadOnlyList<IExpressionTransformer> _globalTransformers;
    private readonly IReadOnlyList<IExpressionTransformer> _contextTransformers;
    private readonly List<IExpressionTransformer>          _mapperTransformers = [];
    private CoverageValidation                             _coverageValidation;

    internal MapperBuilder(
        Expression<Func<TSource, TDest>>? memberInitExpression,
        CoverageValidation coverageValidation = CoverageValidation.NonNullableProperties,
        IReadOnlyList<IExpressionTransformer>? globalTransformers = null,
        IReadOnlyList<IExpressionTransformer>? contextTransformers = null)
    {
        ValidateParameterlessConstructor();
        ValidateMemberInitExpression(memberInitExpression);

        _memberInitExpression = memberInitExpression;
        _coverageValidation   = coverageValidation;
        _globalTransformers   = globalTransformers ?? [];
        _contextTransformers  = contextTransformers ?? [];
    }

    /// <summary>
    /// Creates a builder pre-populated with inherited property mappings from a
    /// base mapper. Used by <see cref="InheritedMapperBuilder{TSource,TBase}.For{TDest}"/>
    /// to support mapper inheritance.
    /// </summary>
    internal MapperBuilder(
        IReadOnlyList<PropertyMapping> inheritedMappings,
        CoverageValidation coverageValidation,
        IReadOnlyList<IExpressionTransformer>? globalTransformers,
        IReadOnlyList<IExpressionTransformer>? contextTransformers)
    {
        ValidateParameterlessConstructor();

        _memberInitExpression = null;
        _mappings.AddRange(inheritedMappings);
        _coverageValidation  = coverageValidation;
        _globalTransformers  = globalTransformers ?? [];
        _contextTransformers = contextTransformers ?? [];
    }

    /// <summary>
    /// Registers one or more per-mapper expression transformers that run after
    /// global and per-context transformers during expression compilation.
    /// </summary>
    public MapperBuilder<TSource, TDest> WithTransformers(params IExpressionTransformer[] transformers)
    {
        _mapperTransformers.AddRange(transformers);
        return this;
    }

    /// <summary>
    /// Overrides the <see cref="CoverageValidation"/> mode for this mapper,
    /// replacing the default inherited from the <see cref="MapperContext"/>.
    /// </summary>
    public MapperBuilder<TSource, TDest> SetCoverageValidation(CoverageValidation mode)
    {
        _coverageValidation = mode;
        return this;
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
    /// property is unaccounted for according to the active
    /// <see cref="CoverageValidation"/> mode.
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
        if (_coverageValidation != CoverageValidation.None)
        {
            var nullabilityCtx = new NullabilityInfoContext();
            List<string> uncovered =
            [
                .. from p in typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   where p.CanWrite && RequiresCoverage(p, nullabilityCtx, _coverageValidation)
                   where !rawMappings.ContainsKey(p.Name)
                   select p.Name
            ];

            if (uncovered.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The following properties of {typeof(TDest).Name} are not mapped: " +
                    string.Join(", ", uncovered));
            }
        }

        // Remove ignored entries — they served their purpose for the coverage check.
        foreach (var key in rawMappings.Where(kv => kv.Value.Kind == MappingKind.Ignored).Select(kv => kv.Key).ToList())
        {
            rawMappings.Remove(key);
        }

        // Source expressions are stored raw. Nested mapper calls (Map / Project) are
        // inlined at expression-build time by NestedMapperInliner, so no detection
        // pass is needed here and nested mappers do not need to exist at build time.

        // Combine transformers: global → per-context → per-mapper
        List<IExpressionTransformer> allTransformers = [.. _globalTransformers, .. _contextTransformers, .. _mapperTransformers];

        return new Mapper<TSource, TDest>([.. rawMappings.Values], allTransformers);
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

    private static void ValidateParameterlessConstructor()
    {
        if (typeof(TDest).GetConstructor(Type.EmptyTypes) == null)
        {
            throw new InvalidOperationException(
                $"Destination type {typeof(TDest).Name} must have a public parameterless constructor. " +
                "Constructor-based mapping is not supported.");
        }
    }

    private static void ValidateMemberInitExpression(
        Expression<Func<TSource, TDest>>? memberInitExpression)
    {
        if (memberInitExpression is null)
        {
            return;
        }

        switch (memberInitExpression.Body)
        {
            case MemberInitExpression:
                return;

            case NewExpression newExpr when newExpr.Arguments.Count > 0:
                throw new InvalidOperationException(
                    $"The member-init expression for {typeof(TDest).Name} uses a parameterized constructor. " +
                    "Only object-initializer syntax (new TDest { Prop = value }) is supported. " +
                    "Constructor-based mapping is not supported.");

            case NewExpression:
                // Parameterless new — valid but produces no mappings
                return;

            default:
                throw new InvalidOperationException(
                    $"The member-init expression for {typeof(TDest).Name} must use object-initializer syntax " +
                    $"(new TDest {{ Prop = value }}), but got: {memberInitExpression.Body.NodeType}.");
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the property must be explicitly covered by
    /// a Map / Optional / Ignore call, according to the specified
    /// <paramref name="mode"/>.
    /// </summary>
    private static bool RequiresCoverage(
        PropertyInfo p,
        NullabilityInfoContext ctx,
        CoverageValidation mode)
    {
        if (mode == CoverageValidation.AllProperties)
        {
            return true;
        }

        // NonNullableProperties mode: non-nullable value type → always required
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
