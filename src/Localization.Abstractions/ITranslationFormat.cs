namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A container-format provider: reads and writes a <see cref="Catalog"/> to and from one on-disk
/// translation format. The reconciler and the runtime depend only on this interface, never on a
/// concrete format.
/// </summary>
public interface ITranslationFormat
{
    /// <summary>The short format identifier (for example <c>"po"</c>, <c>"xliff"</c>, <c>"arb"</c>).</summary>
    public string FormatId { get; }

    /// <summary>The file extensions this format uses, each including the leading dot.</summary>
    public IReadOnlyCollection<string> Extensions { get; }

    /// <summary>The features this format supports.</summary>
    public FormatCapabilities Capabilities { get; }

    /// <summary>
    /// Reads a <see cref="Catalog"/> from <paramref name="input"/>. Parsing is CPU work over an in-hand stream,
    /// so it is synchronous — the bytes are obtained (synchronously or asynchronously) before this is called.
    /// </summary>
    /// <param name="input">The stream to read from.</param>
    /// <returns>The parsed catalog.</returns>
    public Catalog Read(Stream input);

    /// <summary>
    /// Writes <paramref name="catalog"/> to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="catalog">The catalog to serialize.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <param name="options">
    /// How to serialize — the verbose development layout (the default) or the compact publish bundle. A
    /// <see langword="null"/> value is treated as <see cref="CatalogWriteOptions.Default"/>.
    /// </param>
    /// <returns>A task that completes when the catalog has been written.</returns>
    public Task WriteAsync(Stream output, Catalog catalog, CancellationToken cancellationToken, CatalogWriteOptions? options = null);
}
