using System.ComponentModel;
using ArchPillar.Extensions.Localization.Catalogs;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// Flattens the catalogs in scope into one minified bundle per culture for deployment (runtime precedence applied,
/// untranslated entries skipped). An un-customized source language contributes nothing, so it yields no bundle.
/// </summary>
internal sealed class MergeCommand : AsyncCommand<MergeCommand.Settings>
{
    /// <summary>Options for <c>merge</c>.</summary>
    internal sealed class Settings : CatalogScopeSettings
    {
        [CommandOption("--output <DIR>")]
        [Description("The directory to write the per-culture bundles to.")]
        public string? Output { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("The bundle format (default: arb).")]
        public string? Format { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = ScopeInput.Require(settings.Output, "--output");
        IReadOnlyList<string> inputDirectories = CatalogDirectoryResolver.ResolveDirectories(settings.ToScope());
        foreach (var inputDirectory in inputDirectories)
        {
            if (CatalogIo.SamePath(inputDirectory, output))
            {
                return ToolConsole.Fail("--output must differ from the --input location; merging into it would overwrite the source catalogs.");
            }
        }

        ITranslationFormat outputProvider = CatalogIo.FormatOrDefault(string.IsNullOrEmpty(settings.Format) ? "arb" : settings.Format);

        var catalogs = new List<Catalog>();
        foreach (var file in inputDirectories.SelectMany(CatalogNaming.EnumerateCatalogFiles))
        {
            catalogs.Add(CatalogIo.ReadFile(CatalogIo.ProviderFor(file), file));
        }

        // Reuse the runtime's load (precedence, skip untranslated, source loaded as overrides), then dump one bundle
        // per culture, minified — the published bundle is a runtime artifact, not a translator's working file.
        IReadOnlyList<Catalog> merged = CatalogFlattener.Flatten(catalogs);
        await AnsiConsole.Status().StartAsync("Merging…", async ctx =>
        {
            foreach (Catalog catalog in merged)
            {
                ctx.Status($"Merging {catalog.Culture}…");
                await CatalogIo.WriteFileAsync(outputProvider, Path.Combine(output, catalog.Culture + CatalogNaming.Extension(outputProvider)), catalog, new CatalogWriteOptions { Minify = true });
            }
        });

        ToolConsole.Success($"Merged {catalogs.Count} catalog(s) into {merged.Count} bundle(s) in {output}");
        return 0;
    }
}
