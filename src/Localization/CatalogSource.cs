namespace ArchPillar.Extensions.Localization;

/// <summary>
/// How a <see cref="CatalogDescriptor"/>'s <see cref="Catalog"/> is obtained — the place the
/// synchronous/asynchronous distinction lives. A closed two-case discriminated union (modelled as a sealed record
/// hierarchy until C# ships union types): a local source is <see cref="Synchronous"/> (a file or embedded resource,
/// read and parsed in place) and can be produced from the synchronous lookup path; a networked source (the HTTP
/// manifest) is <see cref="Asynchronous"/>, produced only ahead of a lookup, never from inside one. The provider
/// owns the parse, so the store receives a ready catalog and never touches a format itself.
/// </summary>
public abstract record CatalogSource
{
    private CatalogSource()
    {
    }

    /// <summary>A catalog produced synchronously — read from a file or an embedded resource and parsed in place.</summary>
    /// <param name="Open">Produces the parsed catalog.</param>
    public sealed record Synchronous(Func<Catalog> Open) : CatalogSource;

    /// <summary>A catalog produced asynchronously — fetched over the network (the HTTP manifest) and parsed.</summary>
    /// <param name="OpenAsync">Produces the parsed catalog.</param>
    public sealed record Asynchronous(Func<CancellationToken, ValueTask<Catalog>> OpenAsync) : CatalogSource;
}
