using System.Collections.Concurrent;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Creates category-scoped <see cref="ILocalizer"/> instances over a single root <see cref="DefaultLocalizer"/>,
/// mirroring <c>ILoggerFactory</c>. Typed localizers are cached per type, so resolving the same
/// <see cref="ILocalizer{T}"/> repeatedly does not allocate.
/// </summary>
public sealed class LocalizerFactory : ILocalizerFactory
{
    private readonly DefaultLocalizer _localizer;
    private readonly ConcurrentDictionary<Type, ILocalizer> _byType = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizerFactory"/> class over a root localizer.
    /// </summary>
    /// <param name="localizer">The root localizer that holds the loaded overrides.</param>
    /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is <see langword="null"/>.</exception>
    public LocalizerFactory(DefaultLocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <inheritdoc />
    public ILocalizer<T> Create<T>() =>
        (ILocalizer<T>)_byType.GetOrAdd(typeof(T), static (_, root) => new CategoryLocalizer<T>(root), _localizer);

    /// <inheritdoc />
    public ILocalizer Create(string category)
    {
        if (category is null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        return new CategoryLocalizer(_localizer, category);
    }
}
