using System.ComponentModel;
using System.IO.Compression;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// Imports a translator's returned zip, routing each catalog back to its origin assembly and the dev-side format
/// already in use (XLIFF when there is no existing file).
/// </summary>
internal sealed class ImportCommand : AsyncCommand<ImportCommand.Settings>
{
    /// <summary>Options for <c>import</c>.</summary>
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--input <FILE>")]
        [Description("The zip file returned by the translator.")]
        public string? Input { get; init; }

        [CommandOption("--project [PATH]")]
        [Description("A project (.csproj or its directory) whose catalog directory to write into.")]
        public FlagValue<string> Project { get; init; } = new();

        [CommandOption("--solution [PATH]")]
        [Description("A solution (.sln/.slnx or its directory) whose catalog directory to write into.")]
        public FlagValue<string> Solution { get; init; } = new();

        [CommandOption("--catalog-path <PROJECT_SUBPATH>")]
        [Description("The catalog folder inside each project to write into (default: Translations).")]
        public string? CatalogPath { get; init; }

        [CommandOption("--output <DIR>")]
        [Description("Write every catalog into this one directory instead, relative to the current directory; wins over --catalog-path.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var zipPath = ScopeInput.Require(settings.Input, "--input");

        // --input here is the zip to read, not a catalog directory, so the scope carries no Input. With --output
        // every entry lands in that one directory. Otherwise --catalog-path routes each entry back to the project
        // that owns the assembly it names — matching where the authoring commands wrote it — and falls back to the
        // scope's base directory for an entry with no matching project. The project map and base are resolved
        // lazily, so --output needs no scope discovery at all.
        var scope = new ScopeOptions(null, null, ScopeInput.Optional(settings.Project), ScopeInput.Optional(settings.Solution), Recurse: false);
        var flat = string.IsNullOrEmpty(settings.Output) ? null : Path.GetFullPath(settings.Output);
        var folder = string.IsNullOrEmpty(settings.CatalogPath) ? CatalogDirectoryResolver.CatalogFolderName : settings.CatalogPath;
        IReadOnlyDictionary<string, string>? projectDirectories = null;
        string? scopeBase = null;

        string DirectoryFor(string assemblyName)
        {
            if (flat is not null)
            {
                return flat;
            }

            projectDirectories ??= CatalogDirectoryResolver.ProjectDirectoriesByName(scope);
            return projectDirectories.TryGetValue(assemblyName, out var projectDirectory)
                ? Path.Combine(projectDirectory, folder)
                : Path.Combine(scopeBase ??= CatalogDirectoryResolver.ScopeBaseDirectory(scope), folder);
        }

        var imported = 0;
        await ToolConsole.StatusAsync("Importing…", async ctx =>
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ITranslationFormat? source = CatalogIo.Registry.ResolveByExtension(Path.GetExtension(entry.Name));
                if (source is null)
                {
                    continue;
                }

                ToolConsole.Status(ctx, $"Importing {entry.Name}…");
                Catalog catalog;
                using (Stream entryStream = entry.Open())
                using (var buffer = new MemoryStream())
                {
                    await entryStream.CopyToAsync(buffer, cancellationToken);
                    buffer.Position = 0;
                    catalog = source.Read(buffer);
                }

                // The entry name carries the assembly and culture (set by export), so the returned translation lands
                // in its project's folder, in whatever format the repo already uses (XLIFF when there is no file).
                (var name, var culture) = CatalogNaming.Split(Path.GetFileNameWithoutExtension(entry.Name));
                var directory = DirectoryFor(name);
                Directory.CreateDirectory(directory);
                ITranslationFormat target = CatalogIo.ImportTargetProvider(directory, name, culture);
                await CatalogIo.WriteFileAsync(target, Path.Combine(directory, CatalogNaming.FileName(name, culture, target)), catalog);
                imported++;
            }
        });

        ToolConsole.Success($"Imported {imported} catalog(s)");
        return 0;
    }
}
