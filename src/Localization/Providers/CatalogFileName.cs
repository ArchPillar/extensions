namespace ArchPillar.Extensions.Localization.Providers;

/// <summary>
/// The one owner of the catalog file-naming convention: a catalog named <c>{name}.{culture}.{ext}</c> carries
/// its culture as the segment before the extension (<c>App.Web.de.xliff</c> → <c>de</c>), and a bare
/// <c>{culture}.{ext}</c> is just the culture (<c>de.arb</c> → <c>de</c>). Shared by every provider that lists
/// catalogs by file, resource, or URI name, so the rule has a single definition.
/// </summary>
internal static class CatalogFileName
{
    /// <summary>
    /// The culture tag <paramref name="nameOrUri"/> ends with, per the <c>{name}.{culture}.{ext}</c> convention.
    /// A URI query or fragment is stripped first, so a fetched catalog reads its culture from the path only.
    /// </summary>
    /// <param name="nameOrUri">A catalog file path, embedded-resource name, or URI.</param>
    /// <returns>The culture tag, or the whole file name when it carries no <c>.{culture}</c> segment.</returns>
    public static string CultureOf(string nameOrUri)
    {
        var name = Path.GetFileNameWithoutExtension(StripQuery(nameOrUri));
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    /// <summary>
    /// The file extension of <paramref name="nameOrUri"/> (including the leading dot), reading past any URI query
    /// or fragment so a fetched catalog's format is chosen from the path only.
    /// </summary>
    /// <param name="nameOrUri">A catalog file path, embedded-resource name, or URI.</param>
    /// <returns>The extension including the dot, or an empty string when there is none.</returns>
    public static string ExtensionOf(string nameOrUri) => Path.GetExtension(StripQuery(nameOrUri));

    // Drops a URI query/fragment (?v=1, #frag) so the file name and extension are read from the path only; a plain
    // file path has neither, so this is a no-op there.
    private static string StripQuery(string value)
    {
        var end = value.IndexOfAny(['?', '#']);
        return end >= 0 ? value[..end] : value;
    }
}
