using System.IO.Compression;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Reading, writing, and re-serializing catalogs for the commands: the shared format registry, per-path provider
/// resolution, and the lossy-conversion warnings. The registry is stateless, so a single instance is shared.
/// </summary>
internal static class CatalogIo
{
    /// <summary>The built-in formats (XLIFF, ARB, PO); shared, since a format holds no per-call state.</summary>
    public static TranslationFormatRegistry Registry { get; } = BuildRegistry();

    /// <summary>The provider for a path, chosen by its file extension.</summary>
    /// <exception cref="ArgumentException">No registered format recognizes the extension.</exception>
    public static ITranslationFormat ProviderFor(string path) =>
        Registry.ResolveByExtension(Path.GetExtension(path))
        ?? throw new ArgumentException($"No provider for '{path}'.");

    /// <summary>The format id, or the authoring default (XLIFF) when unset; throws on an unknown id.</summary>
    /// <exception cref="ArgumentException"><paramref name="formatId"/> is a non-empty unknown id.</exception>
    public static ITranslationFormat FormatOrDefault(string? formatId) =>
        string.IsNullOrEmpty(formatId)
            ? Registry.ResolveById("xliff")!
            : Registry.ResolveById(formatId) ?? throw new ArgumentException($"Unknown format '{formatId}'.");

    /// <summary>
    /// The dev-side format a returned translation should land as: the format of the existing on-disk catalog for
    /// this assembly and culture, so import round-trips into whatever the repo already uses. An exact
    /// assembly+culture match wins; otherwise a sibling culture of the same assembly fixes the format; with no
    /// existing file the authoring default (XLIFF) is used.
    /// </summary>
    public static ITranslationFormat ImportTargetProvider(string outputDir, string name, string culture)
    {
        ITranslationFormat? sameAssembly = null;
        if (Directory.Exists(outputDir))
        {
            foreach (var file in Directory.EnumerateFiles(outputDir))
            {
                ITranslationFormat? provider = Registry.ResolveByExtension(Path.GetExtension(file));
                if (provider is null)
                {
                    continue;
                }

                (var existingName, var existingCulture) = CatalogNaming.Split(Path.GetFileNameWithoutExtension(file));
                if (!string.Equals(existingName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(existingCulture, culture, StringComparison.OrdinalIgnoreCase))
                {
                    return provider;
                }

                sameAssembly ??= provider;
            }
        }

        return sameAssembly ?? Registry.ResolveById("xliff")!;
    }

    /// <summary>Whether two paths point at the same file (so a convert/merge never overwrites its own source).</summary>
    public static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);

    /// <summary>Reads and parses a catalog file, naming the file in any parse failure so the error is actionable.</summary>
    public static Catalog ReadFile(ITranslationFormat provider, string path)
    {
        using FileStream stream = File.OpenRead(path);
        try
        {
            return provider.Read(stream);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to read '{path}': {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Writes a catalog to a path, creating the directory and carrying the assembly name from the file name into the
    /// catalog's source identity (the published bundle is named by culture alone, so it keeps the format default).
    /// </summary>
    public static async Task WriteFileAsync(ITranslationFormat provider, string path, Catalog catalog, CatalogWriteOptions? options = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        var sourceName = CatalogNaming.Split(Path.GetFileNameWithoutExtension(path)).Name;
        CatalogWriteOptions effective = (options ?? CatalogWriteOptions.Default) with
        {
            SourceName = sourceName.Length == 0 ? null : sourceName
        };
        File.WriteAllBytes(path, await SerializeAsync(provider, catalog, effective));
    }

    /// <summary>Serializes a catalog to bytes in the given format.</summary>
    public static async Task<byte[]> SerializeAsync(ITranslationFormat provider, Catalog catalog, CatalogWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        await provider.WriteAsync(stream, catalog, options);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes the given catalog files into a single zip (replacing any existing one), each re-serialized to the
    /// target format and named <c>{Assembly}.{culture}.{ext}</c>. A duplicate name — a catalog gathered from two
    /// overlapping scope roots — is written once, first match wins.
    /// </summary>
    public static async Task<int> WriteCatalogZipAsync(string zipPath, IEnumerable<string> files, ITranslationFormat target)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            (var name, var culture) = CatalogNaming.Split(Path.GetFileNameWithoutExtension(file));
            var entryName = CatalogNaming.FileName(name, culture, target);
            if (!written.Add(entryName))
            {
                continue;
            }

            Catalog catalog = ReadFile(ProviderFor(file), file);
            var bytes = await SerializeAsync(target, catalog, new CatalogWriteOptions { SourceName = name });
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            await stream.WriteAsync(bytes, 0, bytes.Length);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Warns (to stderr, without failing) for each capability the source format uses but the target lacks, so a
    /// lossy conversion is visible. Plural representation is excluded — the converter bridges native and ICU plurals.
    /// </summary>
    public static void WarnOnLostCapabilities(ITranslationFormat source, ITranslationFormat target, Catalog catalog)
    {
        FormatCapabilities lost = source.Capabilities & ~target.Capabilities;
        foreach (var message in DescribeLosses(lost, catalog))
        {
            ToolConsole.Warn(message);
        }
    }

    private static IEnumerable<string> DescribeLosses(FormatCapabilities lost, Catalog catalog)
    {
        if (lost.HasFlag(FormatCapabilities.Context) && catalog.Entries.Any(entry => !string.IsNullOrEmpty(entry.Context)))
        {
            yield return "target format cannot store a disambiguation context; it will be dropped.";
        }

        if (lost.HasFlag(FormatCapabilities.Comments) && catalog.Entries.Any(entry => !string.IsNullOrEmpty(entry.Comment) || !string.IsNullOrEmpty(entry.TranslatorComment)))
        {
            yield return "target format cannot store comments; developer and translator comments will be dropped.";
        }

        if (lost.HasFlag(FormatCapabilities.SourceReferences) && catalog.Entries.Any(entry => entry.References.Count > 0))
        {
            yield return "target format cannot store source references; they will be dropped.";
        }

        if (lost.HasFlag(FormatCapabilities.PreviousSource) && catalog.Entries.Any(entry => !string.IsNullOrEmpty(entry.PreviousSource)))
        {
            yield return "target format cannot store the previous source; drift history will be dropped.";
        }

        if (lost.HasFlag(FormatCapabilities.ExplicitState) && catalog.Entries.Any(entry => entry.State != TranslationState.Translated))
        {
            yield return "target format has no explicit state field; translation state will be inferred and may be approximate.";
        }
    }

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }
}
