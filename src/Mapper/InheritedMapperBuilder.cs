using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Intermediate fluent builder returned by
/// <see cref="MapperContext.Inherit{TSource,TBase}"/>. Call
/// <see cref="For{TDest}"/> to specify the derived destination type and
/// obtain a <see cref="MapperBuilder{TSource,TDest}"/> pre-populated with
/// the base mapper's property mappings.
/// </summary>
public sealed class InheritedMapperBuilder<TSource, TBase>
{
    private readonly IReadOnlyList<PropertyMapping>          _baseMappings;
    private readonly CoverageValidation                     _coverageValidation;
    private readonly IReadOnlyList<IExpressionTransformer>  _globalTransformers;
    private readonly IReadOnlyList<IExpressionTransformer>  _contextTransformers;

    internal InheritedMapperBuilder(
        IReadOnlyList<PropertyMapping> baseMappings,
        CoverageValidation coverageValidation,
        IReadOnlyList<IExpressionTransformer> globalTransformers,
        IReadOnlyList<IExpressionTransformer> contextTransformers)
    {
        _baseMappings        = baseMappings;
        _coverageValidation  = coverageValidation;
        _globalTransformers  = globalTransformers;
        _contextTransformers = contextTransformers;
    }

    /// <summary>
    /// Creates a <see cref="MapperBuilder{TSource,TDest}"/> for the derived
    /// destination type <typeparamref name="TDest"/>, pre-populated with
    /// all property mappings from the base mapper. Chain additional
    /// <c>.Map()</c>, <c>.Optional()</c>, or <c>.Ignore()</c> calls to
    /// cover properties introduced by <typeparamref name="TDest"/>.
    /// </summary>
    public MapperBuilder<TSource, TDest> For<TDest>() where TDest : TBase
    {
        return new MapperBuilder<TSource, TDest>(
            _baseMappings,
            _coverageValidation,
            _globalTransformers,
            _contextTransformers);
    }

    /// <summary>
    /// Creates a <see cref="MapperBuilder{TDerivedSource,TDest}"/> for a
    /// derived source type <typeparamref name="TDerivedSource"/> and derived
    /// destination type <typeparamref name="TDest"/>, pre-populated with
    /// all property mappings from the base mapper. The inherited source
    /// expressions remain valid because <typeparamref name="TDerivedSource"/>
    /// inherits all members from <typeparamref name="TSource"/>. Chain
    /// additional <c>.Map()</c> calls to map properties introduced by either
    /// derived type.
    /// </summary>
    public MapperBuilder<TDerivedSource, TDest> For<TDerivedSource, TDest>()
        where TDerivedSource : TSource
        where TDest          : TBase
    {
        return new MapperBuilder<TDerivedSource, TDest>(
            _baseMappings,
            _coverageValidation,
            _globalTransformers,
            _contextTransformers);
    }
}
