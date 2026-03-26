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
    /// Registers a global expression transformer that will run on every mapper
    /// expression tree during compilation.
    /// </summary>
    public GlobalMapperOptions AddTransformer(IExpressionTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        _transformers.Add(transformer);
        return this;
    }
}
