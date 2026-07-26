using System.ComponentModel;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// Bundles the target catalogs in scope into a zip for a translator: one <c>&lt;culture&gt;.zip</c> per language, or
/// a single zip when a language is named. The source culture is excluded (it is the origin, not a target).
/// </summary>
internal sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    /// <summary>Options for <c>export</c>.</summary>
    internal sealed class Settings : CatalogScopeSettings
    {
        [CommandOption("--lang <CULTURE>")]
        [Description("Export a single culture to one zip; omit to export every non-source culture.")]
        public string? Lang { get; init; }

        [CommandOption("--output <PATH>")]
        [Description("The zip file (with --lang) or the directory of per-culture zips.")]
        public string? Output { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("The format the bundled catalogs are written as (default: xliff).")]
        public string? Format { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = ScopeInput.Require(settings.Output, "--output");
        var language = !string.IsNullOrEmpty(settings.Lang) ? settings.Lang : null;
        var sourceCulture = settings.Source;
        ITranslationFormat target = CatalogIo.FormatOrDefault(settings.Format);

        IReadOnlyList<string> directories = CatalogDirectoryResolver.ResolveDirectories(settings.ToScope());
        var matched = directories
            .SelectMany(CatalogNaming.EnumerateCatalogFiles)
            .Where(file => language is null
                ? !string.Equals(CatalogNaming.CultureOf(file), sourceCulture, StringComparison.OrdinalIgnoreCase)
                : string.Equals(CatalogNaming.CultureOf(file), language, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matched.Count == 0)
        {
            return ToolConsole.Fail(language is null
                ? "No target catalogs found in scope."
                : $"No '{language}' catalogs found in scope.");
        }

        // With --lang, --output is a single zip for that one culture. Without it, --output is a directory and each
        // culture gets its own <culture>.zip, so every language can go to a different translator.
        if (language is not null)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent!);
            }

            var written = await ToolConsole.StatusAsync(
                $"Exporting {language}…",
                _ => CatalogIo.WriteCatalogZipAsync(output, matched, target));
            ToolConsole.Success($"Exported {written} '{language}' catalog(s) to {output}");
            return 0;
        }

        Directory.CreateDirectory(output);
        var total = 0;
        var languages = 0;
        await ToolConsole.StatusAsync("Exporting…", async ctx =>
        {
            foreach (IGrouping<string, string> group in matched.GroupBy(CatalogNaming.CultureOf, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                ToolConsole.Status(ctx, $"Exporting {group.Key}…");
                total += await CatalogIo.WriteCatalogZipAsync(Path.Combine(output, group.Key + ".zip"), group, target);
                languages++;
            }
        });

        ToolConsole.Success($"Exported {total} catalog(s) across {languages} language(s) to {output}");
        return 0;
    }
}
