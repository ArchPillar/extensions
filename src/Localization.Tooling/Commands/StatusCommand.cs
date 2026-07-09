using System.ComponentModel;
using System.Globalization;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>Reports the extractable strings per in-scope assembly, and (with a catalog directory) the per-language translation coverage.</summary>
internal sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    /// <summary>Options for <c>status</c>.</summary>
    internal sealed class Settings : AuthoringScopeSettings
    {
        [CommandOption("--output <DIR>")]
        [Description("A catalog directory to also report per-language translation counts from.")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var sourceLanguage = settings.Source;
        var rows = new List<(string Name, int Keys, string Languages)>();
        var totalKeys = 0;
        await ScopeRunner.ForEachTemplateAsync(settings, settings.Output, "Scanning", (name, catalogDirectory, template) =>
        {
            totalKeys += template.Entries.Count;
            var languages = Directory.Exists(catalogDirectory)
                ? DescribeLanguages(catalogDirectory, name, sourceLanguage, template.Entries.Count)
                : string.Empty;
            rows.Add((name, template.Entries.Count, languages));
            return Task.CompletedTask;
        });

        if (rows.Count == 0)
        {
            ToolConsole.Info("No assemblies with localizable strings found in the given scope. Build first, then point --input/--project/--solution at the output.");
            return 0;
        }

        var showTranslations = rows.Any(row => row.Languages.Length > 0);
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Assembly");
        table.AddColumn(new TableColumn("Strings").RightAligned());
        if (showTranslations)
        {
            table.AddColumn("Translations");
        }

        foreach ((var name, var keys, var languages) in rows)
        {
            var count = keys.ToString(CultureInfo.InvariantCulture);
            if (showTranslations)
            {
                table.AddRow(name, count, languages);
            }
            else
            {
                table.AddRow(name, count);
            }
        }

        AnsiConsole.Write(table);
        ToolConsole.Info($"{rows.Count} assembly(ies), {totalKeys} string(s) total, source {sourceLanguage}.");
        return 0;
    }

    private static string DescribeLanguages(string catalogDirectory, string assemblyName, string sourceLanguage, int keyCount)
    {
        var parts = new List<string>();
        foreach (var path in CatalogNaming.TargetCatalogsFor(catalogDirectory, assemblyName, sourceLanguage))
        {
            var culture = CatalogNaming.Split(Path.GetFileNameWithoutExtension(path)).Culture;
            Catalog catalog = CatalogIo.ReadFile(CatalogIo.ProviderFor(path), path);
            var translated = catalog.Entries.Count(entry => entry.State == TranslationState.Translated);
            parts.Add($"{culture} {translated}/{keyCount}");
        }

        return string.Join(", ", parts);
    }
}
