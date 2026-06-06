namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Creates <see cref="ILocalizer"/> instances by category, mirroring <c>ILoggerFactory</c>. The generic
/// overload is the common path (category from the type); the string overload is the dynamic escape hatch,
/// the equivalent of <c>ILoggerFactory.CreateLogger(string)</c>.
/// </summary>
public interface ILocalizerFactory
{
    /// <summary>
    /// Creates a localizer whose category is the full name of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type whose full name is the translation category.</typeparam>
    /// <returns>The scoped localizer.</returns>
    public ILocalizer<T> Create<T>();

    /// <summary>
    /// Creates a localizer for an explicit <paramref name="category"/>, for the rare case where the
    /// category is computed rather than a type.
    /// </summary>
    /// <param name="category">The translation category.</param>
    /// <returns>The localizer for that category.</returns>
    public ILocalizer Create(string category);
}
