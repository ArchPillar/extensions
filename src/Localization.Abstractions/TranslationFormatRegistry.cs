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
    /// Determines whether <paramref name="obj"/> is a registry with the same format support — the same format ids
    /// mapped to the same format types. Construction order and instance identity do not matter, so two registries
    /// built from the same formats (the built-in set, say, which is assembled fresh each time) compare equal.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when both registries support the same formats.</returns>
    public override bool Equals(object? obj) =>
        obj is TranslationFormatRegistry other && HasSameFormats(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Order-independent: the id set identifies the registry, so XOR the per-id hashes rather than depend on
        // the dictionary's enumeration order.
        var hash = 0;
        foreach (var id in _byId.Keys)
        {
            hash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(id);
        }

        return hash;
    }

    // Whether both registries resolve the same format ids to formats of the same type. Extensions derive from the
    // format (stateless, so type-determined), so matching the id-to-type mapping matches the extension mapping too.
    private bool HasSameFormats(TranslationFormatRegistry other)
    {
        if (_byId.Count != other._byId.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, ITranslationFormat> entry in _byId)
        {
            if (!other._byId.TryGetValue(entry.Key, out ITranslationFormat? format) || format.GetType() != entry.Value.GetType())
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string extension) =>
        extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
}
