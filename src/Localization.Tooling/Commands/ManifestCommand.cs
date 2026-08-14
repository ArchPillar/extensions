using System.ComponentModel;
using System.Text.Json;
using ArchPillar.Extensions.Localization.Providers;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// Writes the catalog index the HTTP runtime loader reads (apl-catalogs.json), listing every catalog in scope by
/// culture and file name — over HTTP there is no directory to enumerate, so the index is how the client discovers
/// what to fetch. Run after extract (dev layout) and again after merge (published layout).
/// </summary>
internal sealed class ManifestCommand : AsyncCommand<ManifestCommand.Settings>
{
    /// <summary>Options for <c>manifest</c>.</summary>
    internal sealed class Settings : CatalogScopeSettings
    {
        [CommandOption("--output <FILE>")]
        [Description("The manifest file to write; defaults to apl-catalogs.json in the first catalog directory.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> inputDirectories = CatalogDirectoryResolver.ResolveDirectories(settings.ToScope());
        if (inputDirectories.Count == 0)
        {
            return ToolConsole.Fail("No catalog directory found in scope.");
        }

        var output = !string.IsNullOrEmpty(settings.Output)
            ? settings.Output
            : Path.Combine(inputDirectories[0], ManifestCatalogProvider.DefaultManifestFileName);

        var entries = new List<(string Culture, string File)>();
        foreach (var file in inputDirectories.SelectMany(CatalogNaming.EnumerateCatalogFiles))
        {
            var culture = CatalogNaming.CultureOf(file);

            // An unparseable name (no culture segment) is skipped rather than guessed at.
            if (string.IsNullOrEmpty(culture))
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

        CatalogIo.WriteIfChanged(output, BuildManifest(entries));
        ToolConsole.Success($"Wrote manifest with {entries.Count} catalog(s) to {output}");
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
}
