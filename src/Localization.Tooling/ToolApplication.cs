using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;
using Spectre.Console;

namespace ArchPillar.Extensions.Localization.Tooling;

/// <summary>
/// The <c>dotnet apl</c> command-line surface: <c>extract</c> the baked template from a built
/// assembly, <c>add</c> a new language, <c>sync</c> a target against the template, and <c>convert</c>
/// between formats. Every command works on explicit paths and runs on demand, never as part of a build.
/// </summary>
internal static class ToolApplication
{
    private const string TemplateAttribute = "ArchPillar.Extensions.Localization.GeneratedLocalizationTemplateAttribute";

    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return Usage();
        }

        Dictionary<string, string> options = ParseOptions(arguments);
        try
        {
            return arguments[0] switch
            {
                "extract" => await ExtractAsync(options).ConfigureAwait(false),
                "add" => await AddAsync(arguments, options).ConfigureAwait(false),
                "sync" => await SyncAsync(options).ConfigureAwait(false),
                "convert" => await ConvertAsync(options).ConfigureAwait(false),
                _ => Fail($"Unknown command '{arguments[0]}'.")
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
        if (arguments.Length < 2)
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
            return current.AsSpan().SequenceEqual(updated) ? 0 : Fail($"'{targetPath}' is out of date.");
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
        TranslationFormatRegistry registry = BuildRegistry();

        ITranslationFormat target = registry.ResolveById(toFormat) ?? throw new ArgumentException($"Unknown format '{toFormat}'.");
        Catalog catalog = await ReadFileAsync(ProviderFor(registry, from), from).ConfigureAwait(false);
        await WriteFileAsync(target, output, catalog).ConfigureAwait(false);
        Success($"Converted {from} to {output}");
        return 0;
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

    private static async Task<Catalog> ReadFileAsync(ITranslationFormat provider, string path)
    {
        using FileStream stream = File.OpenRead(path);
        return await provider.ReadAsync(stream, CancellationToken.None).ConfigureAwait(false);
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

    private static void Success(string message) => AnsiConsole.MarkupLineInterpolated($"[green]done[/] {message}");

    private static int Fail(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {message}");
        return 1;
    }

    private static int Usage()
    {
        AnsiConsole.MarkupLine("[bold]dotnet apl[/] [grey]<extract|add|sync|convert> [options][/]");
        return 1;
    }
}
