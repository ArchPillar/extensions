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
    /// Determines whether <paramref name="obj"/> is a registry with the same format support — the same ids and the
    /// same file extensions, each mapped to the same format type. Construction order and instance identity do not
    /// matter, so two registries built from the same formats (the built-in set, say, which is assembled fresh each
    /// time) compare equal, while a registry that resolves an extension to a different format does not. Per-instance
    /// parsing behaviour within one format type is not compared (formats are treated as identified by type).
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

    // Whether both registries resolve the same ids and the same extensions to formats of the same type. Extensions
    // are compared explicitly (not assumed to follow from the id set) because a format's extensions are its own
    // choice, so two formats sharing an id and type can still register different extensions.
    private bool HasSameFormats(TranslationFormatRegistry other) =>
        SameTypeMapping(_byId, other._byId) && SameTypeMapping(_byExtension, other._byExtension);

    // Whether two id/extension-to-format maps have the same keys, each mapped to a format of the same type.
    private static bool SameTypeMapping(Dictionary<string, ITranslationFormat> left, Dictionary<string, ITranslationFormat> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, ITranslationFormat> entry in left)
        {
            if (!right.TryGetValue(entry.Key, out ITranslationFormat? format) || format.GetType() != entry.Value.GetType())
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string extension) =>
        extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
}
