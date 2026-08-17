using System.ComponentModel;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>Changes a single catalog file's format, warning about anything the target format cannot represent.</summary>
internal sealed class ConvertCommand : AsyncCommand<ConvertCommand.Settings>
{
    /// <summary>Options for <c>convert</c>.</summary>
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--from <FILE>")]
        [Description("The catalog file to convert.")]
        public string? From { get; init; }

        [CommandOption("--to <FORMAT>")]
        [Description("The target format id (arb, xliff, po).")]
        public string? To { get; init; }

        [CommandOption("--output <FILE>")]
        [Description("The file to write the converted catalog to.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var from = ScopeInput.Require(settings.From, "--from");
        var toFormat = ScopeInput.Require(settings.To, "--to");
        var output = ScopeInput.Require(settings.Output, "--output");
        if (CatalogIo.SamePath(from, output))
        {
            return ToolConsole.Fail("--output must differ from --from; converting in place would overwrite the source file.");
        }

        ITranslationFormat source = CatalogIo.ProviderFor(from);
        ITranslationFormat target = CatalogIo.FormatOrDefault(toFormat);
        Catalog catalog = CatalogIo.ReadFile(source, from);
        CatalogIo.WarnOnLostCapabilities(source, target, catalog);
        await CatalogIo.WriteFileAsync(target, output, catalog, cancellationToken: cancellationToken);
        ToolConsole.Success($"Converted {from} → {output}");
        return 0;
    }
}
