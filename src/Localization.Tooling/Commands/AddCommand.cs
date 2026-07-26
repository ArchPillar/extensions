using System.ComponentModel;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>Creates a new language catalog for a template (single-file mode) or across a scope (per assembly).</summary>
internal sealed class AddCommand : AsyncCommand<AddCommand.Settings>
{
    /// <summary>Options for <c>add</c>.</summary>
    internal sealed class Settings : AuthoringScopeSettings
    {
        [CommandArgument(0, "<CULTURE>")]
        [Description("The language (culture) to add.")]
        public string Language { get; init; } = string.Empty;

        [CommandOption("--format <FORMAT>")]
        [Description("The catalog format to write (arb, xliff, po; default: xliff).")]
        public string? Format { get; init; }

        [CommandOption("--template <FILE>")]
        [Description("A single source template to add the language beside, instead of a scope.")]
        public string? Template { get; init; }

        [CommandOption("--force")]
        [Description("Overwrite an existing language catalog instead of skipping it.")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var language = settings.Language;
        var force = settings.Force;

        if (!string.IsNullOrEmpty(settings.Template))
        {
            var templatePath = settings.Template;
            var output = !string.IsNullOrEmpty(settings.Output) ? settings.Output : Path.GetDirectoryName(templatePath)!;
            ITranslationFormat provider = CatalogIo.ProviderFor(templatePath);
            Catalog template = CatalogIo.ReadFile(provider, templatePath);
            (var name, _) = CatalogNaming.Split(Path.GetFileNameWithoutExtension(templatePath));
            var target = Path.Combine(output, CatalogNaming.FileName(name, language, provider));
            if (File.Exists(target) && !force)
            {
                return ToolConsole.Fail($"'{target}' already exists; pass --force to overwrite.");
            }

            await CatalogIo.WriteFileAsync(provider, target, Reconciler.CreateLanguage(template, language));
            ToolConsole.Success($"Added {language} at {target}");
            return 0;
        }

        ITranslationFormat scopeProvider = CatalogIo.FormatOrDefault(settings.Format);
        var created = 0;
        var skipped = 0;
        await ScopeRunner.ForEachTemplateAsync(settings, $"Adding {language}", async (name, catalogDirectory, template) =>
        {
            var target = Path.Combine(catalogDirectory, CatalogNaming.FileName(name, language, scopeProvider));

            // Skip an existing language file rather than overwrite it — re-creating would reset every
            // translation to NeedsTranslation. Updating an existing language is `sync`'s job.
            if (File.Exists(target) && !force)
            {
                skipped++;
                return;
            }

            await CatalogIo.WriteFileAsync(scopeProvider, target, Reconciler.CreateLanguage(template, language));
            created++;
        });

        if (created == 0 && skipped == 0)
        {
            ToolConsole.Info("No translatable strings found in scope; nothing to add.");
            return 0;
        }

        ToolConsole.Success($"Added {language} for {created} assembly catalog(s){(skipped > 0 ? $"; skipped {skipped} existing (use --force to overwrite)" : string.Empty)}");
        return 0;
    }
}
