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
    /// Writes a catalog to a path, skipping the write when the bytes would be identical. For the commands that build
    /// their output from somewhere else (a different file, a zip, many catalogs merged): they never parsed a prior
    /// version of this path, so the file itself is the only available baseline.
    /// </summary>
    public static async Task WriteFileAsync(
        ITranslationFormat provider,
        string path,
        Catalog catalog,
        CatalogWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await WriteIfDifferentAsync(path, await SerializeAsync(provider, catalog, EffectiveOptions(path, options)), cancellationToken);
    }

    /// <summary>
    /// Writes bytes only when they differ from what is on the path, comparing after the line-ending adaptation so a
    /// checkout convention never reads as a change. Costs one read of the destination and no parse — the baseline for
    /// generated output (the publish bundles, the catalog manifest), which is regenerated on every build and must not
    /// have its timestamp moved when nothing changed.
    /// </summary>
    public static async Task WriteIfDifferentAsync(string path, byte[] serialized, CancellationToken cancellationToken = default)
    {
        var content = LineEndings.Apply(serialized, LineEndings.For(path));
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            return;
        }

        await WriteRawAsync(path, content, cancellationToken);
    }

    /// <summary>
    /// The bytes to write when reconciling <paramref name="existing"/> into <paramref name="updated"/> actually
    /// changed something this format persists, or <see langword="null"/> when it did not.
    /// <para>
    /// Both catalogs are serialized with identical options and compared in memory, so the decision needs no read of
    /// the file: the parse that produced <paramref name="existing"/> is the only time it is read. Comparing the
    /// serialized forms rather than the models is what makes this safe across formats — PO and XLIFF do not persist
    /// placeholders, so a model comparison would see a difference the file could never hold and rewrite the catalog
    /// on every run (see <see cref="Reconciler"/>).
    /// </para>
    /// </summary>
    public static async Task<byte[]?> PendingWriteAsync(
        ITranslationFormat provider,
        string path,
        Catalog updated,
        Catalog existing,
        CatalogWriteOptions? options = null)
    {
        CatalogWriteOptions effective = EffectiveOptions(path, options);
        var after = await SerializeAsync(provider, updated, effective);
        var before = await SerializeAsync(provider, existing, effective);
        return before.AsSpan().SequenceEqual(after) ? null : after;
    }

    /// <summary>
    /// Writes bytes unconditionally, in the line ending the path should use, creating the directory. For callers that
    /// have already established there is a change — <see cref="PendingWriteAsync"/> — so re-checking the file would
    /// be the redundant read this design exists to avoid.
    /// </summary>
    public static async Task WriteBytesAsync(string path, byte[] serialized, CancellationToken cancellationToken = default) =>
        await WriteRawAsync(path, LineEndings.Apply(serialized, LineEndings.For(path)), cancellationToken);

    private static async Task WriteRawAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    // The assembly name from the file name becomes the catalog's source identity (the published bundle is named by
    // culture alone, so it keeps the format default). Shared by the write and the comparison so both serialize
    // identically — a differing SourceName would otherwise read as a content change.
    private static CatalogWriteOptions EffectiveOptions(string path, CatalogWriteOptions? options)
    {
        var sourceName = CatalogNaming.Split(Path.GetFileNameWithoutExtension(path)).Name;
        return (options ?? CatalogWriteOptions.Default) with
        {
            SourceName = sourceName.Length == 0 ? null : sourceName
        };
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
        registry.Register(new AplocTranslationFormat());
        return registry;
    }
}
