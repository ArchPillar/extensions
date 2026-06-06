namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Resolves <see cref="ITranslationFormat"/> providers by format id or file extension. This is an
/// instance type, not a global registry: the runtime and the tooling each construct one and register
/// the providers they ship with, so formats stay genuinely pluggable with no static state.
/// </summary>
public sealed class TranslationFormatRegistry
{
    private readonly Dictionary<string, ITranslationFormat> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ITranslationFormat> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers <paramref name="format"/> under its id and each of its extensions, replacing any
    /// previously registered provider for the same id or extension.
    /// </summary>
    /// <param name="format">The provider to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> is <see langword="null"/>.</exception>
    public void Register(ITranslationFormat format)
    {
        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        _byId[format.FormatId] = format;
        foreach (var extension in format.Extensions)
        {
            _byExtension[Normalize(extension)] = format;
        }
    }

    /// <summary>
    /// Resolves the provider registered for <paramref name="formatId"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="formatId">The format id to resolve.</param>
    /// <returns>The provider, or <see langword="null"/> when none is registered.</returns>
    public ITranslationFormat? ResolveById(string formatId) =>
        formatId is not null && _byId.TryGetValue(formatId, out ITranslationFormat? format) ? format : null;

    /// <summary>
    /// Resolves the provider registered for <paramref name="extension"/> (with or without a leading
    /// dot), or <see langword="null"/>.
    /// </summary>
    /// <param name="extension">The file extension to resolve.</param>
    /// <returns>The provider, or <see langword="null"/> when none is registered.</returns>
    public ITranslationFormat? ResolveByExtension(string extension) =>
        extension is not null && _byExtension.TryGetValue(Normalize(extension), out ITranslationFormat? format)
            ? format
            : null;

    /// <summary>
    /// Gets the registered providers.
    /// </summary>
    public IReadOnlyCollection<ITranslationFormat> Formats => _byId.Values;

    private static string Normalize(string extension) =>
        extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
}
