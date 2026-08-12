using System.ComponentModel;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// Reconciles language catalogs against a freshly extracted template (a single target, or every target across a
/// scope). With <c>--check</c> it reports drift (exit 1) instead of writing, for a CI gate.
/// </summary>
internal sealed class SyncCommand : AsyncCommand<SyncCommand.Settings>
{
    /// <summary>Options for <c>sync</c>.</summary>
    internal sealed class Settings : AuthoringScopeSettings
    {
        [CommandOption("--template <FILE>")]
        [Description("The source template to reconcile against (single-target mode).")]
        public string? Template { get; init; }

        [CommandOption("--target <FILE>")]
        [Description("The language catalog to reconcile (single-target mode).")]
        public string? Target { get; init; }

        [CommandOption("--check")]
        [Description("Report drift (exit 1) without writing, for a CI gate.")]
        public bool Check { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var check = settings.Check;

        if (!string.IsNullOrEmpty(settings.Template) || !string.IsNullOrEmpty(settings.Target))
        {
            var templatePath = ScopeInput.Require(settings.Template, "--template");
            var targetPath = ScopeInput.Require(settings.Target, "--target");
            Catalog template = CatalogIo.ReadFile(CatalogIo.ProviderFor(templatePath), templatePath);
            var targetName = CatalogNaming.Split(Path.GetFileNameWithoutExtension(targetPath)).Name;
            var drifted = await SyncTargetAsync(template, targetPath, targetName.Length == 0 ? null : targetName, check);
            if (check)
            {
                return drifted ? ToolConsole.Drift($"'{targetPath}' is out of date; run sync to update it.") : 0;
            }

            ToolConsole.Success($"Synced {targetPath}");
            return 0;
        }

        var sourceLanguage = settings.Source;
        var driftedTargets = new List<string>();
        var synced = 0;
        var any = false;
        await ScopeRunner.ForEachTemplateAsync(settings, "Syncing", async (name, catalogDirectory, template) =>
        {
            any = true;
            foreach (var targetPath in CatalogNaming.TargetCatalogsFor(catalogDirectory, name, sourceLanguage))
            {
                if (await SyncTargetAsync(template, targetPath, name, check))
                {
                    driftedTargets.Add(targetPath);
                }
                else if (!check)
                {
                    synced++;
                }
            }
        });

        if (!any)
        {
            ToolConsole.Info("No translatable strings found in scope; nothing to sync.");
            return 0;
        }

        if (check)
        {
            return driftedTargets.Count == 0
                ? 0
                : ToolConsole.Drift($"{driftedTargets.Count} catalog(s) out of date ({string.Join(", ", driftedTargets.Select(Path.GetFileName))}); run sync to update them.");
        }

        ToolConsole.Success($"Synced {synced} catalog(s)");
        return 0;
    }

    // Reconciles one target catalog against the template, then writes it (returning false) or, with check, compares
    // the serialized bytes without writing (returning true when it is out of date). The single owner of a sync step,
    // shared by the single-target and scope-mode paths.
    private static async Task<bool> SyncTargetAsync(Catalog template, string targetPath, string? sourceName, bool check)
    {
        ITranslationFormat targetProvider = CatalogIo.ProviderFor(targetPath);
        Catalog reconciled = Reconciler.Reconcile(template, CatalogIo.ReadFile(targetProvider, targetPath));
        var serialized = await CatalogIo.SerializeAsync(targetProvider, reconciled, new CatalogWriteOptions { SourceName = sourceName });
        // Adapt to the target's existing line endings so a repo that checks catalogs out with CRLF neither reports
        // false drift under --check nor gets rewritten to LF, which would be a line-ending-only diff every run.
        var updated = CatalogIo.MatchLineEndings(targetPath, serialized);
        if (check)
        {
            return !File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(updated);
        }

        File.WriteAllBytes(targetPath, updated);
        return false;
    }
}
