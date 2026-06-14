using System.IO.Compression;
using System.Text.Json;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;
using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling;

/// <summary>
/// The <c>dotnet apl</c> command-line surface. Authoring commands (<c>status</c>, <c>extract</c>, <c>add</c>,
/// <c>sync</c>) work over a project / solution / directory scope so a whole app is handled at once; the
/// translator handover commands (<c>export</c>, <c>import</c>) bundle per-assembly catalogs to and from a zip;
/// <c>convert</c> changes a single file's format; <c>merge</c> flattens a set of catalogs into one bundle
/// per culture for deployment; and <c>manifest</c> writes the catalog index the HTTP runtime loader reads when
/// there is no readable file system (Blazor WebAssembly). Every command works on explicit paths and runs on
/// demand, never as part of a build. Dev/source catalogs are named <c>{AssemblyName}.{culture}.{ext}</c> so
/// catalogs from different assemblies never collide and a translation can always be routed back to its origin.
/// </summary>
internal static class ToolApplication
{
    private static readonly IReadOnlyCollection<string> _scopeOptions = ["--assembly", "--input", "--project", "--solution", "--recurse"];

    // The options each command accepts. An option outside its command's set is rejected rather than
    // silently ignored, so a typo (e.g. "--chek" for "--check") cannot turn a read-only check into a write.
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _knownOptions =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            ["status"] = Scope("--source", "--output"),
            ["extract"] = Scope("--source", "--format", "--output"),
            ["add"] = Scope("--source", "--format", "--template", "--output", "--force"),
            ["sync"] = Scope("--source", "--template", "--target", "--output", "--check"),
            ["convert"] = new HashSet<string>(StringComparer.Ordinal) { "--from", "--to", "--output" },
            ["export"] = new HashSet<string>(StringComparer.Ordinal) { "--input", "--lang", "--output", "--format" },
            ["import"] = new HashSet<string>(StringComparer.Ordinal) { "--input", "--output" },
            ["merge"] = new HashSet<string>(StringComparer.Ordinal) { "--input", "--output", "--format", "--source" },
            ["manifest"] = new HashSet<string>(StringComparer.Ordinal) { "--input", "--output", "--source" }
        };

    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return Usage();
        }

        var command = arguments[0];
        if (!_knownOptions.TryGetValue(command, out IReadOnlyCollection<string>? allowed))
        {
            return Fail($"Unknown command '{command}'.");
        }

        Dictionary<string, string> options = ParseOptions(arguments);
        var unknown = options.Keys.FirstOrDefault(key => !allowed.Contains(key));
        if (unknown is not null)
        {
            return Fail($"Unknown option '{unknown}' for '{command}'.");
        }

        try
        {
            return command switch
            {
                "status" => await StatusAsync(options).ConfigureAwait(false),
                "extract" => await ExtractAsync(options).ConfigureAwait(false),
                "add" => await AddAsync(arguments, options).ConfigureAwait(false),
                "sync" => await SyncAsync(options).ConfigureAwait(false),
                "convert" => await ConvertAsync(options).ConfigureAwait(false),
                "export" => await ExportAsync(options).ConfigureAwait(false),
                "import" => await ImportAsync(options).ConfigureAwait(false),
                "merge" => await MergeAsync(options).ConfigureAwait(false),
                "manifest" => await ManifestAsync(options).ConfigureAwait(false),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static async Task<int> StatusAsync(IReadOnlyDictionary<string, string> options)
    {
        var sourceLanguage = SourceLanguage(options);
        TranslationFormatRegistry registry = BuildRegistry();
        var catalogDirectory = options.TryGetValue("--output", out var dir) ? dir : null;
        var totalKeys = 0;
        var found = 0;
        using var extractor = new AssemblyStringExtractor();
        foreach (var path in ScopeResolver.Resolve(ParseScope(options)))
        {
            Catalog? template = TemplateBuilder.Build(extractor, path, sourceLanguage);
            if (template is null)
            {
                continue;
            }

            found++;
            var name = Path.GetFileNameWithoutExtension(path);
            totalKeys += template.Entries.Count;
            Console.Out.WriteLine($"{name}  (source {sourceLanguage})  {template.Entries.Count} string(s)");
            if (catalogDirectory is not null)
            {
                await ReportLanguageStateAsync(registry, catalogDirectory, name, sourceLanguage, template.Entries.Count).ConfigureAwait(false);
            }
        }

        if (found == 0)
        {
            Console.Out.WriteLine("No assemblies with localizable strings found in the given scope. Build first, then point --input/--project/--solution at the output.");
            return 0;
        }

        Console.Out.WriteLine($"{found} assembly(ies), {totalKeys} string(s) total.");
        return 0;
    }

    private static async Task ReportLanguageStateAsync(TranslationFormatRegistry registry, string catalogDirectory, string assemblyName, string sourceLanguage, int keyCount)
    {
        foreach (var path in TargetCatalogsFor(catalogDirectory, assemblyName, sourceLanguage, registry))
        {
            (_, var culture) = SplitCatalogName(Path.GetFileNameWithoutExtension(path));
            Catalog catalog = await ReadFileAsync(ProviderFor(registry, path), path).ConfigureAwait(false);
            var translated = catalog.Entries.Count(entry => entry.State == TranslationState.Translated);
            Console.Out.WriteLine($"    {culture}: {translated}/{keyCount} translated");
        }
    }

    private static async Task<int> ExtractAsync(IReadOnlyDictionary<string, string> options)
    {
        var output = Require(options, "--output");
        var sourceLanguage = SourceLanguage(options);
        TranslationFormatRegistry registry = BuildRegistry();
        ITranslationFormat provider = FormatOf(options, registry);
        var count = 0;
        using var extractor = new AssemblyStringExtractor();
        foreach (var path in ScopeResolver.Resolve(ParseScope(options)))
        {
            Catalog? template = TemplateBuilder.Build(extractor, path, sourceLanguage);
            if (template is null)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            await WriteFileAsync(provider, Path.Combine(output, CatalogFileName(name, sourceLanguage, provider)), template).ConfigureAwait(false);
            count++;
        }

        if (count == 0)
        {
            // No strings is a valid state (a project may simply have none), not an error — the per-build
            // extract runs on every project, so this must be a clean no-op rather than a failure.
            Console.Out.WriteLine("No translatable strings found in scope; nothing to extract.");
            return 0;
        }

        Success($"Extracted {count} template(s) to {output}");
        return 0;
    }

    private static async Task<int> AddAsync(string[] arguments, IReadOnlyDictionary<string, string> options)
    {
        // The language is the first positional argument; an option in its place (e.g. "add --template …")
        // means it was omitted, which would otherwise create a junk "--template.arb" file and exit 0.
        if (arguments.Length < 2 || arguments[1].StartsWith("--", StringComparison.Ordinal))
        {
            return Fail("Usage: add <lang> (--input|--project|--solution) --output <dir> [--force]  |  add <lang> --template <path> --output <dir> [--force]");
        }

        var language = arguments[1];
        var force = options.ContainsKey("--force");
        TranslationFormatRegistry registry = BuildRegistry();

        if (options.ContainsKey("--template"))
        {
            var templatePath = Require(options, "--template");
            var output = options.TryGetValue("--output", out var dir) ? dir : Path.GetDirectoryName(templatePath)!;
            ITranslationFormat provider = ProviderFor(registry, templatePath);
            Catalog template = await ReadFileAsync(provider, templatePath).ConfigureAwait(false);
            (var name, _) = SplitCatalogName(Path.GetFileNameWithoutExtension(templatePath));
            var target = Path.Combine(output, CatalogFileName(name, language, provider));
            if (File.Exists(target) && !force)
            {
                return Fail($"'{target}' already exists; pass --force to overwrite.");
            }

            await WriteFileAsync(provider, target, Reconciler.CreateLanguage(template, language)).ConfigureAwait(false);
            Success($"Added {language} at {target}");
            return 0;
        }

        var outputDir = Require(options, "--output");
        var sourceLanguage = SourceLanguage(options);
        ITranslationFormat scopeProvider = FormatOf(options, registry);
        var created = 0;
        var skipped = 0;
        using var extractor = new AssemblyStringExtractor();
        foreach (var path in ScopeResolver.Resolve(ParseScope(options)))
        {
            Catalog? template = TemplateBuilder.Build(extractor, path, sourceLanguage);
            if (template is null)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var target = Path.Combine(outputDir, CatalogFileName(name, language, scopeProvider));
            // Skip an existing language file rather than overwrite it — re-creating would reset every
            // translation to NeedsTranslation. Updating an existing language is `sync`'s job.
            if (File.Exists(target) && !force)
            {
                skipped++;
                continue;
            }

            await WriteFileAsync(scopeProvider, target, Reconciler.CreateLanguage(template, language)).ConfigureAwait(false);
            created++;
        }

        if (created == 0 && skipped == 0)
        {
            Console.Out.WriteLine("No translatable strings found in scope; nothing to add.");
            return 0;
        }

        Success($"Added {language} for {created} assembly catalog(s){(skipped > 0 ? $"; skipped {skipped} existing (use --force to overwrite)" : string.Empty)}");
        return 0;
    }

    private static async Task<int> SyncAsync(IReadOnlyDictionary<string, string> options)
    {
        TranslationFormatRegistry registry = BuildRegistry();
        var check = options.ContainsKey("--check");

        if (options.ContainsKey("--template") || options.ContainsKey("--target"))
        {
            var templatePath = Require(options, "--template");
            var targetPath = Require(options, "--target");
            Catalog template = await ReadFileAsync(ProviderFor(registry, templatePath), templatePath).ConfigureAwait(false);
            ITranslationFormat targetProvider = ProviderFor(registry, targetPath);
            Catalog reconciled = Reconciler.Reconcile(template, await ReadFileAsync(targetProvider, targetPath).ConfigureAwait(false));
            var updated = await SerializeAsync(targetProvider, reconciled).ConfigureAwait(false);
            if (check)
            {
                var current = File.ReadAllBytes(targetPath);
                return current.AsSpan().SequenceEqual(updated) ? 0 : Drift($"'{targetPath}' is out of date; run sync to update it.");
            }

            File.WriteAllBytes(targetPath, updated);
            Success($"Synced {targetPath}");
            return 0;
        }

        var outputDir = Require(options, "--output");
        var sourceLanguage = SourceLanguage(options);
        var drifted = new List<string>();
        var synced = 0;
        var any = false;
        using var extractor = new AssemblyStringExtractor();
        foreach (var path in ScopeResolver.Resolve(ParseScope(options)))
        {
            Catalog? template = TemplateBuilder.Build(extractor, path, sourceLanguage);
            if (template is null)
            {
                continue;
            }

            any = true;
            var name = Path.GetFileNameWithoutExtension(path);
            foreach (var targetPath in TargetCatalogsFor(outputDir, name, sourceLanguage, registry))
            {
                ITranslationFormat targetProvider = ProviderFor(registry, targetPath);
                Catalog reconciled = Reconciler.Reconcile(template, await ReadFileAsync(targetProvider, targetPath).ConfigureAwait(false));
                var updated = await SerializeAsync(targetProvider, reconciled).ConfigureAwait(false);
                if (check)
                {
                    if (!File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(updated))
                    {
                        drifted.Add(targetPath);
                    }
                }
                else
                {
                    File.WriteAllBytes(targetPath, updated);
                    synced++;
                }
            }
        }

        if (!any)
        {
            Console.Out.WriteLine("No translatable strings found in scope; nothing to sync.");
            return 0;
        }

        if (check)
        {
            return drifted.Count == 0
                ? 0
                : Drift($"{drifted.Count} catalog(s) out of date ({string.Join(", ", drifted.Select(Path.GetFileName))}); run sync to update them.");
        }

        Success($"Synced {synced} catalog(s) in {outputDir}");
        return 0;
    }

    private static async Task<int> ConvertAsync(IReadOnlyDictionary<string, string> options)
    {
        var from = Require(options, "--from");
        var toFormat = Require(options, "--to");
        var output = Require(options, "--output");
        if (SamePath(from, output))
        {
            return Fail("--output must differ from --from; converting in place would overwrite the source file.");
        }

        TranslationFormatRegistry registry = BuildRegistry();

        ITranslationFormat source = ProviderFor(registry, from);
        ITranslationFormat target = registry.ResolveById(toFormat) ?? throw new ArgumentException($"Unknown format '{toFormat}'.");
        Catalog catalog = await ReadFileAsync(source, from).ConfigureAwait(false);
        WarnOnLostCapabilities(source, target, catalog);
        await WriteFileAsync(target, output, catalog).ConfigureAwait(false);
        Success($"Converted {from} to {output}");
        return 0;
    }

    private static async Task<int> ExportAsync(IReadOnlyDictionary<string, string> options)
    {
        var catalogDirectory = Require(options, "--input");
        var language = Require(options, "--lang");
        var outputZip = Require(options, "--output");
        var formatId = options.TryGetValue("--format", out var f) && f.Length > 0 ? f : "xliff";
        TranslationFormatRegistry registry = BuildRegistry();
        ITranslationFormat target = registry.ResolveById(formatId) ?? throw new ArgumentException($"Unknown format '{formatId}'.");

        var matched = EnumerateCatalogFiles(catalogDirectory, registry)
            .Where(file => string.Equals(CultureOf(file), language, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matched.Count == 0)
        {
            return Fail($"No '{language}' catalogs found in '{catalogDirectory}'.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputZip));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        if (File.Exists(outputZip))
        {
            File.Delete(outputZip);
        }

        using (ZipArchive zip = ZipFile.Open(outputZip, ZipArchiveMode.Create))
        {
            foreach (var file in matched)
            {
                Catalog catalog = await ReadFileAsync(ProviderFor(registry, file), file).ConfigureAwait(false);
                (var name, _) = SplitCatalogName(Path.GetFileNameWithoutExtension(file));
                var bytes = await SerializeAsync(target, catalog).ConfigureAwait(false);
                ZipArchiveEntry entry = zip.CreateEntry(CatalogFileName(name, language, target), CompressionLevel.Optimal);
                using Stream stream = entry.Open();
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
        }

        Success($"Exported {matched.Count} '{language}' catalog(s) to {outputZip}");
        return 0;
    }

    private static async Task<int> ImportAsync(IReadOnlyDictionary<string, string> options)
    {
        var zipPath = Require(options, "--input");
        var outputDir = Require(options, "--output");
        TranslationFormatRegistry registry = BuildRegistry();
        ITranslationFormat arb = registry.ResolveById("arb")!;
        Directory.CreateDirectory(outputDir);

        var imported = 0;
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ITranslationFormat? source = registry.ResolveByExtension(Path.GetExtension(entry.Name));
            if (source is null)
            {
                continue;
            }

            Catalog catalog;
            using (Stream entryStream = entry.Open())
            using (var buffer = new MemoryStream())
            {
                await entryStream.CopyToAsync(buffer).ConfigureAwait(false);
                buffer.Position = 0;
                catalog = await source.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            }

            // Route the returned translation back to its origin and the dev format: the entry name carries the
            // assembly and culture (set by export), so {AssemblyName}.{culture}.arb lands beside the others.
            (var name, var culture) = SplitCatalogName(Path.GetFileNameWithoutExtension(entry.Name));
            await WriteFileAsync(arb, Path.Combine(outputDir, CatalogFileName(name, culture, arb)), catalog).ConfigureAwait(false);
            imported++;
        }

        Success($"Imported {imported} catalog(s) to {outputDir}");
        return 0;
    }

    // Surfaces what the target format cannot represent. FormatCapabilities is otherwise inert; here it lets a
    // lossy conversion warn (to stderr, without failing) for each capability the source uses but the target
    // lacks. Plural representation is excluded — the converter bridges native and ICU plurals, keeping any
    // unrepresentable plural verbatim — so only genuinely droppable data is reported.
    private static void WarnOnLostCapabilities(ITranslationFormat source, ITranslationFormat target, Catalog catalog)
    {
        FormatCapabilities lost = source.Capabilities & ~target.Capabilities;
        foreach (var message in DescribeLosses(lost, catalog))
        {
            Console.Error.WriteLine("warning: " + message);
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

    private static async Task<int> MergeAsync(IReadOnlyDictionary<string, string> options)
    {
        var input = Require(options, "--input");
        var output = Require(options, "--output");
        var inputDirectory = File.Exists(input) ? Path.GetDirectoryName(Path.GetFullPath(input))! : input;
        if (SamePath(inputDirectory, output))
        {
            return Fail("--output must differ from the --input location; merging into it would overwrite the source catalogs.");
        }

        var formatId = options.TryGetValue("--format", out var f) && f.Length > 0 ? f : "arb";
        var sourceCulture = options.TryGetValue("--source", out var s) && s.Length > 0 ? s : "en";
        TranslationFormatRegistry registry = BuildRegistry();
        ITranslationFormat outputProvider = registry.ResolveById(formatId) ?? throw new ArgumentException($"Unknown format '{formatId}'.");

        var catalogs = new List<Catalog>();
        foreach (var file in EnumerateCatalogFiles(input, registry))
        {
            catalogs.Add(await ReadFileAsync(ProviderFor(registry, file), file).ConfigureAwait(false));
        }

        // Reuse the runtime's load (precedence + skip source/untranslated), then dump one bundle per culture.
        IReadOnlyList<Catalog> merged = CatalogLoader.Flatten(catalogs, new LocalizerOptions { SourceCulture = sourceCulture });
        foreach (Catalog catalog in merged)
        {
            await WriteFileAsync(outputProvider, Path.Combine(output, catalog.Culture + Extension(outputProvider)), catalog).ConfigureAwait(false);
        }

        Success($"Merged {catalogs.Count} catalog(s) into {merged.Count} bundle(s) in {output}");
        return 0;
    }

    // Writes the catalog index the HTTP runtime loader reads (apl-catalogs.json), listing every non-source
    // catalog in the directory by culture and file name. Run after extract (dev layout, {AssemblyName}.{culture})
    // and again after merge (published layout, {culture}), so the one manifest path resolves in both — over HTTP
    // there is no directory to enumerate, so the index is how the client discovers what to fetch.
    private static async Task<int> ManifestAsync(IReadOnlyDictionary<string, string> options)
    {
        var input = Require(options, "--input");
        if (!Directory.Exists(input))
        {
            return Fail($"--input directory '{input}' does not exist.");
        }

        var sourceCulture = SourceLanguage(options);
        var output = options.TryGetValue("--output", out var o) && o.Length > 0
            ? o
            : Path.Combine(input, HttpCatalogLoaderExtensions.DefaultManifestFileName);
        TranslationFormatRegistry registry = BuildRegistry();

        var entries = new List<(string Culture, string File)>();
        foreach (var file in EnumerateCatalogFiles(input, registry))
        {
            var culture = CultureOf(file);

            // The source-language catalog ships in code as the terminal fallback, so it is never an override to
            // fetch; an unparseable name (no culture segment) is skipped rather than guessed at.
            if (string.IsNullOrEmpty(culture) || string.Equals(culture, sourceCulture, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add((culture, Path.GetFileName(file)));
        }

        entries.Sort((left, right) =>
        {
            var byCulture = string.CompareOrdinal(left.Culture, right.Culture);
            return byCulture != 0 ? byCulture : string.CompareOrdinal(left.File, right.File);
        });

        await File.WriteAllBytesAsync(output, BuildManifest(entries)).ConfigureAwait(false);
        Success($"Wrote manifest with {entries.Count} catalog(s) to {output}");
        return 0;
    }

    private static byte[] BuildManifest(IReadOnlyList<(string Culture, string File)> entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("catalogs");
            foreach ((var culture, var file) in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("culture", culture);
                writer.WriteString("file", file);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    private static IEnumerable<string> EnumerateCatalogFiles(string input, TranslationFormatRegistry registry)
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
            if (registry.ResolveByExtension(Path.GetExtension(file)) is not null)
            {
                yield return file;
            }
        }
    }

    // The dev/source catalog naming: {AssemblyName}.{culture}.{ext}, or {culture}.{ext} when there is no
    // assembly prefix (the legacy single-file shape). The inverse is SplitCatalogName.
    private static string CatalogFileName(string assemblyName, string culture, ITranslationFormat provider) =>
        string.IsNullOrEmpty(assemblyName)
            ? culture + Extension(provider)
            : assemblyName + "." + culture + Extension(provider);

    // Splits a catalog file's base name (no extension) into its assembly prefix and culture: "App.Core.de"
    // -> ("App.Core", "de"), "de" -> ("", "de"). Culture tags never contain '.', so the last segment is it.
    private static (string Name, string Culture) SplitCatalogName(string baseName)
    {
        var lastDot = baseName.LastIndexOf('.');
        return lastDot > 0 ? (baseName[..lastDot], baseName[(lastDot + 1)..]) : (string.Empty, baseName);
    }

    private static string CultureOf(string path) => SplitCatalogName(Path.GetFileNameWithoutExtension(path)).Culture;

    // The target catalogs for one assembly in a directory: files named {AssemblyName}.{culture}.{ext} whose
    // culture is not the source language (the extracted template is not a sync target).
    private static IEnumerable<string> TargetCatalogsFor(string directory, string assemblyName, string sourceLanguage, TranslationFormatRegistry registry)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (registry.ResolveByExtension(Path.GetExtension(file)) is null)
            {
                continue;
            }

            (var name, var culture) = SplitCatalogName(Path.GetFileNameWithoutExtension(file));
            if (string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(culture, sourceLanguage, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static string SourceLanguage(IReadOnlyDictionary<string, string> options) =>
        options.TryGetValue("--source", out var source) && source.Length > 0 ? source : "en";

    private static ITranslationFormat FormatOf(IReadOnlyDictionary<string, string> options, TranslationFormatRegistry registry) =>
        (options.TryGetValue("--format", out var format) && format.Length > 0 ? registry.ResolveById(format) : null)
        ?? registry.ResolveById("arb")!;

    private static ScopeOptions ParseScope(IReadOnlyDictionary<string, string> options) => new(
        options.TryGetValue("--assembly", out var assembly) ? assembly : null,
        options.TryGetValue("--input", out var input) ? input : null,
        options.TryGetValue("--project", out var project) ? project : null,
        options.TryGetValue("--solution", out var solution) ? solution : null,
        options.ContainsKey("--recurse"));

    private static HashSet<string> Scope(params string[] extra) =>
        [.. _scopeOptions, .. extra];

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }

    private static ITranslationFormat ProviderFor(TranslationFormatRegistry registry, string path) =>
        registry.ResolveByExtension(Path.GetExtension(path))
        ?? throw new ArgumentException($"No provider for '{path}'.");

    private static string Extension(ITranslationFormat provider) => provider.Extensions.First();

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);

    private static async Task<Catalog> ReadFileAsync(ITranslationFormat provider, string path)
    {
        using FileStream stream = File.OpenRead(path);
        try
        {
            return await provider.ReadAsync(stream, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A parse failure from the provider names no file; prepend the path so the error is actionable.
            throw new InvalidOperationException($"Failed to read '{path}': {exception.Message}", exception);
        }
    }

    private static async Task WriteFileAsync(ITranslationFormat provider, string path, Catalog catalog)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        File.WriteAllBytes(path, await SerializeAsync(provider, catalog).ConfigureAwait(false));
    }

    private static async Task<byte[]> SerializeAsync(ITranslationFormat provider, Catalog catalog)
    {
        using var stream = new MemoryStream();
        await provider.WriteAsync(stream, catalog, CancellationToken.None).ConfigureAwait(false);
        return stream.ToArray();
    }

    private static Dictionary<string, string> ParseOptions(string[] arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Length; index++)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var hasValue = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);
            options[arguments[index]] = hasValue ? arguments[++index] : string.Empty;
        }

        return options;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new ArgumentException($"Missing required option '{name}'.");

    private static void Success(string message) => Console.Out.WriteLine("done " + message);

    // Exit codes follow the diff/check convention so a CI gate can tell the two apart: 0 = success,
    // 1 = the catalog drifted (an expected, actionable "diff" outcome of sync --check), 2 = an error
    // (bad invocation, missing/malformed file). Errors and drift both go to stderr so they survive a
    // redirected stdout.
    private static int Drift(string message)
    {
        Console.Error.WriteLine("drift: " + message);
        return 1;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 2;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("dotnet apl <status|extract|add|sync|export|import|convert|merge|manifest> [options]");
        Console.Error.WriteLine("  scope (status/extract/add/sync): defaults to the project/solution in the current directory;");
        Console.Error.WriteLine("    or --assembly <dll> | --input <dir> | --project [<csproj>|<dir>] [--recurse] | --solution [<sln>|<dir>]");
        return 2;
    }
}
