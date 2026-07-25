namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// The dev/source catalog file-naming convention: <c>{AssemblyName}.{culture}.{ext}</c>, so catalogs from different
/// assemblies never collide and a translation can always be routed back to its origin. The one owner of composing,
/// splitting, and enumerating catalog file names.
/// </summary>
internal static class CatalogNaming
{
    /// <summary>The file extension a format serializes to (its first registered extension).</summary>
    public static string Extension(ITranslationFormat provider) => provider.Extensions.First();

    /// <summary>
    /// Composes a catalog file name: <c>{AssemblyName}.{culture}.{ext}</c>, or <c>{culture}.{ext}</c> when there is
    /// no assembly prefix (the published bundle shape). The inverse is <see cref="Split"/>.
    /// </summary>
    public static string FileName(string assemblyName, string culture, ITranslationFormat provider) =>
        string.IsNullOrEmpty(assemblyName)
            ? culture + Extension(provider)
            : assemblyName + "." + culture + Extension(provider);

    /// <summary>
    /// Splits a catalog file's base name (no extension) into its assembly prefix and culture: <c>App.Core.de</c> →
    /// <c>("App.Core", "de")</c>, <c>de</c> → <c>("", "de")</c>. Culture tags never contain <c>.</c>, so the last
    /// segment is the culture.
    /// </summary>
    public static (string Name, string Culture) Split(string baseName)
    {
        var lastDot = baseName.LastIndexOf('.');
        return lastDot > 0 ? (baseName[..lastDot], baseName[(lastDot + 1)..]) : (string.Empty, baseName);
    }

    /// <summary>The culture tag a catalog file's name carries.</summary>
    public static string CultureOf(string path) => Split(Path.GetFileNameWithoutExtension(path)).Culture;

    /// <summary>
    /// The catalog files under a directory a registered format recognizes; a single file passes straight through.
    /// </summary>
    public static IEnumerable<string> EnumerateCatalogFiles(string input)
    {
        if (File.Exists(input))
        {
            yield return input;
            yield break;
        }

        if (!Directory.Exists(input))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(input))
        {
            if (CatalogIo.Registry.ResolveByExtension(Path.GetExtension(file)) is not null)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The target catalogs for one assembly in a directory: files named <c>{AssemblyName}.{culture}.{ext}</c> whose
    /// culture is not the source language (the extracted template is not a sync target).
    /// </summary>
    public static IEnumerable<string> TargetCatalogsFor(string directory, string assemblyName, string sourceLanguage)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (CatalogIo.Registry.ResolveByExtension(Path.GetExtension(file)) is null)
            {
                continue;
            }

            (var name, var culture) = Split(Path.GetFileNameWithoutExtension(file));
            if (string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(culture, sourceLanguage, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}
