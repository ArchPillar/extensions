namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Controls how an <see cref="ITranslationFormat"/> serializes a <see cref="Catalog"/>, selecting between
/// the verbose, human- and tool-facing development layout and the compact runtime bundle.
/// </summary>
public sealed record CatalogWriteOptions
{
    /// <summary>The default: the pretty-printed, fully annotated development layout.</summary>
    public static readonly CatalogWriteOptions Default = new();

    /// <summary>
    /// Produce the compact publish bundle: drop insignificant whitespace and every annotation the runtime
    /// does not read (comments, references, placeholders, state, fingerprints, and the preserved source),
    /// keeping only the identity each entry needs to be resolved. Used by the publish-time merge.
    /// </summary>
    public bool Minify { get; init; }

    /// <summary>
    /// Always emit the source text, even when it equals the value — the self-describing source catalog, so a
    /// copywriter who edits the value still has the original it was based on in the file. Ignored by formats
    /// that store the source and translation separately (they always carry both).
    /// </summary>
    public bool AlwaysWriteSource { get; init; }
}
