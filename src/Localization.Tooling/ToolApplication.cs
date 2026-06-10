using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization.Tooling;

/// <summary>
/// The <c>dotnet apl</c> command-line surface: <c>extract</c> the baked template from a built
/// assembly, <c>add</c> a new language, <c>sync</c> a target against the template, <c>convert</c>
/// between formats, and <c>merge</c> a set of catalogs into one flattened bundle per culture (the publish
/// step). Every command works on explicit paths and runs on demand, never as part of a build.
/// </summary>
internal static class ToolApplication
{
    private const string TemplateAttribute = "ArchPillar.Extensions.Localization.GeneratedLocalizationTemplateAttribute";

    // The options each command accepts. An option outside its command's set is rejected rather than
    // silently ignored, so a typo (e.g. "--chek" for "--check") cannot turn a read-only check into a write.
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _knownOptions =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            ["extract"] = new HashSet<string>(StringComparer.Ordinal) { "--assembly", "--output" },
            ["add"] = new HashSet<string>(StringComparer.Ordinal) { "--template", "--output", "--force" },
            ["sync"] = new HashSet<string>(StringComparer.Ordinal) { "--template", "--target", "--check" },
            ["convert"] = new HashSet<string>(StringComparer.Ordinal) { "--from", "--to", "--output" },
            ["merge"] = new HashSet<string>(StringComparer.Ordinal) { "--input", "--output", "--format", "--source" }
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
                "extract" => await ExtractAsync(options).ConfigureAwait(false),
                "add" => await AddAsync(arguments, options).ConfigureAwait(false),
                "sync" => await SyncAsync(options).ConfigureAwait(false),
                "convert" => await ConvertAsync(options).ConfigureAwait(false),
                "merge" => await MergeAsync(options).ConfigureAwait(false),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static async Task<int> ExtractAsync(IReadOnlyDictionary<string, string> options)
    {
        var assemblyPath = Require(options, "--assembly");
        var output = Require(options, "--output");
        (var format, var sourceLanguage, var arb) = ReadBakedTemplate(assemblyPath);

        TranslationFormatRegistry registry = BuildRegistry();
        Catalog catalog = await ReadAsync(registry.ResolveById("arb")!, new MemoryStream(Encoding.UTF8.GetBytes(arb))).ConfigureAwait(false);
        catalog = catalog with { Culture = sourceLanguage };

        ITranslationFormat provider = registry.ResolveById(format) ?? registry.ResolveById("arb")!;
        await WriteFileAsync(provider, Path.Combine(output, sourceLanguage + Extension(provider)), catalog).ConfigureAwait(false);
        Success($"Extracted template for {sourceLanguage} to {output}");
        return 0;
    }

    private static async Task<int> AddAsync(string[] arguments, IReadOnlyDictionary<string, string> options)
    {
        // The language is the first positional argument; an option in its place (e.g. "add --template …")
        // means it was omitted, which would otherwise create a junk "--template.arb" file and exit 0.
        if (arguments.Length < 2 || arguments[1].StartsWith("--", StringComparison.Ordinal))
        {
            return Fail("Usage: add <lang> --template <path> --output <dir> [--force]");
        }

        var language = arguments[1];
        var templatePath = Require(options, "--template");
        var output = options.TryGetValue("--output", out var dir) ? dir : Path.GetDirectoryName(templatePath)!;
        TranslationFormatRegistry registry = BuildRegistry();

        ITranslationFormat provider = ProviderFor(registry, templatePath);
        Catalog template = await ReadFileAsync(provider, templatePath).ConfigureAwait(false);
        var target = Path.Combine(output, language + Extension(provider));
        if (File.Exists(target) && !options.ContainsKey("--force"))
        {
            return Fail($"'{target}' already exists; pass --force to overwrite.");
        }

        await WriteFileAsync(provider, target, Reconciler.CreateLanguage(template, language)).ConfigureAwait(false);
        Success($"Added {language} at {target}");
        return 0;
    }

    private static async Task<int> SyncAsync(IReadOnlyDictionary<string, string> options)
    {
        var templatePath = Require(options, "--template");
        var targetPath = Require(options, "--target");
        TranslationFormatRegistry registry = BuildRegistry();

        Catalog template = await ReadFileAsync(ProviderFor(registry, templatePath), templatePath).ConfigureAwait(false);
        ITranslationFormat targetProvider = ProviderFor(registry, targetPath);
        Catalog reconciled = Reconciler.Reconcile(template, await ReadFileAsync(targetProvider, targetPath).ConfigureAwait(false));

        var updated = await SerializeAsync(targetProvider, reconciled).ConfigureAwait(false);
        if (options.ContainsKey("--check"))
        {
            var current = File.ReadAllBytes(targetPath);
            return current.AsSpan().SequenceEqual(updated) ? 0 : Drift($"'{targetPath}' is out of date; run sync to update it.");
        }

        File.WriteAllBytes(targetPath, updated);
        Success($"Synced {targetPath}");
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

    private static (string Format, string SourceLanguage, string Arb) ReadBakedTemplate(string assemblyPath)
    {
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        foreach (CustomAttributeData data in assembly.GetCustomAttributesData())
        {
            if (data.AttributeType.FullName == TemplateAttribute && data.ConstructorArguments.Count == 3)
            {
                var format = (string)data.ConstructorArguments[0].Value!;
                var sourceLanguage = (string)data.ConstructorArguments[1].Value!;
                var arb = Encoding.UTF8.GetString(Convert.FromBase64String((string)data.ConstructorArguments[2].Value!));
                return (format, sourceLanguage, arb);
            }
        }

        throw new InvalidOperationException($"No baked localization template found in '{assemblyPath}'.");
    }

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

    private static async Task<Catalog> ReadAsync(ITranslationFormat provider, Stream stream)
    {
        using (stream)
        {
            return await provider.ReadAsync(stream, CancellationToken.None).ConfigureAwait(false);
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
        Console.Error.WriteLine("dotnet apl <extract|add|sync|convert|merge> [options]");
        return 2;
    }
}
