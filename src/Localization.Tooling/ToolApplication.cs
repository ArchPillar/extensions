using ArchPillar.Extensions.Localization.Tooling.Commands;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling;

/// <summary>
/// The <c>dotnet apl</c> command-line surface. Authoring commands (<c>status</c>, <c>extract</c>, <c>add</c>,
/// <c>sync</c>) work over a project / solution / directory scope so a whole app is handled at once; the translator
/// handover commands (<c>export</c>, <c>import</c>) bundle per-assembly catalogs to and from a zip; <c>convert</c>
/// changes a single file's format; <c>merge</c> flattens a set of catalogs into one bundle per culture for
/// deployment; and <c>manifest</c> writes the catalog index the HTTP runtime loader reads. Every command works on
/// explicit paths and runs on demand, never as part of a build.
/// </summary>
internal static class ToolApplication
{
    /// <summary>
    /// Runs the tool. Command failures surface as the diff/check exit codes (0 success, 1 drift, 2 error);
    /// a bad invocation (unknown command/option, missing argument, unreadable file) is reported on stderr as an
    /// error and exits 2.
    /// </summary>
    public static async Task<int> RunAsync(string[] arguments)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("apl");

            // Own the failure surface: a parse or runtime error propagates here so it is reported as our "error:"
            // convention on stderr (exit 2), rather than Spectre's own console rendering.
            config.PropagateExceptions();

            // Reject an unrecognized option rather than collecting it as a remaining argument, so a typo (e.g.
            // "--chek" for "--check") cannot silently turn a read-only check into a write.
            config.UseStrictParsing();

            // One place applies --verbose, before whichever command runs: the flag is on all of them, and the
            // console it switches is shared, so a command that had to turn the log on itself would be adding a
            // line that says nothing about what that command does.
            config.SetInterceptor(new VerbosityInterceptor());

            config.AddCommand<StatusCommand>("status")
                .WithDescription("Report the extractable strings per assembly, and translation coverage.");
            config.AddCommand<ExtractCommand>("extract")
                .WithDescription("Extract the source template from each in-scope assembly.");
            config.AddCommand<AddCommand>("add")
                .WithDescription("Create a new language catalog for a template or across a scope.");
            config.AddCommand<SyncCommand>("sync")
                .WithDescription("Reconcile language catalogs against a fresh template (--check for a CI gate).");
            config.AddCommand<ConvertCommand>("convert")
                .WithDescription("Change a single catalog file's format.");
            config.AddCommand<ExportCommand>("export")
                .WithDescription("Bundle the target catalogs in scope into a zip for a translator.");
            config.AddCommand<ImportCommand>("import")
                .WithDescription("Import a translator's returned zip, routing catalogs back to their origin.");
            config.AddCommand<MergeCommand>("merge")
                .WithDescription("Flatten catalogs into one minified bundle per culture for deployment.");
            config.AddCommand<ManifestCommand>("manifest")
                .WithDescription("Write the catalog index the HTTP runtime loader reads.");
        });

        // A bare invocation is a misuse (the tool is run per operation, never idly): show the command list, but exit
        // non-zero so a script that forgot its command fails.
        if (arguments.Length == 0)
        {
            await app.RunAsync(["--help"]);
            return 2;
        }

        try
        {
            return await app.RunAsync(arguments);
        }
        catch (Exception exception)
        {
            return ToolConsole.Fail(exception.Message);
        }
    }

    // Applies the parsed --verbose to the console the whole tool logs through, once per invocation and before the
    // command starts, so everything a command does is inside the log it asked for.
    private sealed class VerbosityInterceptor : ICommandInterceptor
    {
        public void Intercept(CommandContext context, CommandSettings settings)
        {
            if (settings is ToolSettings { Verbose: true })
            {
                ToolConsole.EnableVerbose();
            }
        }
    }
}
