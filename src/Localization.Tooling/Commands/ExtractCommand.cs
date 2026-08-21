using System.ComponentModel;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>Extracts the source template from each in-scope assembly, merging into any existing source catalog.</summary>
internal sealed class ExtractCommand : AsyncCommand<ExtractCommand.Settings>
{
    /// <summary>Options for <c>extract</c>.</summary>
    internal sealed class Settings : AuthoringScopeSettings
    {
        [CommandOption("--format <FORMAT>")]
        [Description("The catalog format to write (arb, xliff, po; default: xliff).")]
        public string? Format { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var sourceLanguage = settings.Source;
        ITranslationFormat provider = CatalogIo.FormatOrDefault(settings.Format);
        var count = 0;
        _ = await ScopeRunner.ForEachTemplateAsync(settings, "Extracting", async (name, catalogDirectory, template) =>
        {
            var sourcePath = Path.Combine(catalogDirectory, CatalogNaming.FileName(name, sourceLanguage, provider));

            // Merge into the existing source catalog rather than overwrite it, so it is a stable, git-tracked
            // artifact whose hand-edited source wording survives a re-extract. A first extract has no file yet,
            // so it merges into an empty source catalog (a clean seed). The source catalog is self-describing:
            // every entry carries source_text, even un-edited echoes, so an editor still has the original.
            Catalog existing = File.Exists(sourcePath)
                ? CatalogIo.ReadFile(provider, sourcePath)
                : new Catalog { Culture = sourceLanguage, Entries = [] };
            var options = new CatalogWriteOptions { AlwaysWriteSource = true };

            // A re-extract that changed nothing leaves the catalog untouched: this runs on every build, and
            // rewriting identical content would move the timestamp incremental builds and watchers key off.
            var pending = await CatalogIo.PendingWriteAsync(
                provider, sourcePath, Reconciler.ReconcileSource(template, existing), existing, options);
            if (pending is not null)
            {
                await CatalogIo.WriteBytesAsync(sourcePath, pending, cancellationToken);
            }

            count++;
        });

        if (count == 0)
        {
            // No strings is a valid state (a project may simply have none), not an error — the per-build extract
            // runs on every project, so this must be a clean no-op rather than a failure.
            ToolConsole.Info("No translatable strings found in scope; nothing to extract.");
            return 0;
        }

        ToolConsole.Success($"Extracted {count} template(s)");
        return 0;
    }
}
