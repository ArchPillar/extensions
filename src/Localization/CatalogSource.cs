namespace ArchPillar.Extensions.Localization;

/// <summary>
/// How a <see cref="CatalogDescriptor"/>'s bytes are obtained — the place the synchronous/asynchronous
/// distinction lives. A closed two-case discriminated union (modelled as a sealed record hierarchy until C#
/// ships union types): a local source is <see cref="Synchronous"/> and can be opened from the synchronous lookup
/// path; a networked source (the HTTP manifest) is <see cref="Asynchronous"/> and is loaded only ahead of a
/// lookup, never from inside one.
/// </summary>
public abstract record CatalogSource
{
    private CatalogSource()
    {
    }

    /// <summary>A catalog whose bytes open synchronously — a file, an embedded resource.</summary>
    /// <param name="Open">Opens the catalog's bytes; the caller owns and disposes the returned stream.</param>
    public sealed record Synchronous(Func<Stream> Open) : CatalogSource;

    /// <summary>A catalog whose bytes are fetched asynchronously — the HTTP manifest.</summary>
    /// <param name="OpenAsync">Opens the catalog's bytes; the caller owns and disposes the returned stream.</param>
    public sealed record Asynchronous(Func<CancellationToken, ValueTask<Stream>> OpenAsync) : CatalogSource;
}
