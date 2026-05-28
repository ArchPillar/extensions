namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Holds expression transformers that apply globally to all mappers across
/// all <see cref="MapperContext"/> instances. Register a single instance as a
/// singleton in your DI container and pass it to your
/// <see cref="MapperContext"/> subclass constructors.
/// <para>
/// Global transformers run first, before per-context and per-mapper
/// transformers.
/// </para>
/// </summary>
public sealed class GlobalMapperOptions
{
    private readonly List<IExpressionTransformer> _transformers = [];

    /// <summary>
    /// Gets the registered global expression transformers, in registration order.
    /// </summary>
    public IReadOnlyList<IExpressionTransformer> Transformers => _transformers;

    /// <summary>
    /// Gets the global default coverage-validation mode applied to every mapper
    /// across all <see cref="MapperContext"/> instances, unless a context overrides
    /// <see cref="MapperContext.DefaultCoverageValidation"/> or an individual mapper
    /// calls <see cref="MapperBuilder{TSource,TDest}.SetCoverageValidation"/>.
    /// Defaults to <see cref="CoverageValidation.NonNullableProperties"/>.
    /// </summary>
    public CoverageValidation DefaultCoverageValidation { get; private set; } = CoverageValidation.NonNullableProperties;

    /// <summary>
    /// Registers a global expression transformer that will run on every mapper
    /// expression tree during compilation.
    /// </summary>
    public GlobalMapperOptions AddTransformer(IExpressionTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        _transformers.Add(transformer);
        return this;
    }

    /// <summary>
    /// Sets the global default coverage-validation mode for every mapper across all
    /// contexts. A context-level <see cref="MapperContext.DefaultCoverageValidation"/>
    /// override or a per-mapper
    /// <see cref="MapperBuilder{TSource,TDest}.SetCoverageValidation"/> call still
    /// takes precedence over this value.
    /// </summary>
    public GlobalMapperOptions SetCoverageValidation(CoverageValidation mode)
    {
        DefaultCoverageValidation = mode;
        return this;
    }
}
