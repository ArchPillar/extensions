using System.ComponentModel;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>Shared helpers for the command settings: reading the scope options into a <see cref="ScopeOptions"/>,
/// and requiring a named option's value.</summary>
internal static class ScopeInput
{
    /// <summary>An optional-value flag as the three scope states: absent (null), present-no-value (""), or a path.</summary>
    public static string? Optional(FlagValue<string> flag) =>
        flag.IsSet ? flag.Value ?? string.Empty : null;

    /// <summary>A required option's value, or an error naming it.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
    public static string Require(string? value, string option) =>
        string.IsNullOrEmpty(value)
            ? throw new ArgumentException($"Missing required option '{option}'.")
            : value;
}

/// <summary>
/// The options every scoped command shares: a project/solution to discover (or the current directory by default),
/// whether to recurse into referenced projects, and the source language. The two concrete scopes add the piece that
/// differs — the authoring commands an <c>--assembly</c>, <c>--no-annotations</c> and the destination pair, each
/// with its own <c>--input</c> meaning (a build-output directory vs a catalog directory).
/// </summary>
internal abstract class ScopeSettings : ToolSettings
{
    [CommandOption("--project [PATH]")]
    [Description("A project (.csproj or its directory); defaults to the current directory.")]
    public FlagValue<string> Project { get; init; } = new();

    [CommandOption("--solution [PATH]")]
    [Description("A solution (.sln/.slnx), a solution filter (.slnf), or a directory; defaults to the current directory.")]
    public FlagValue<string> Solution { get; init; } = new();

    [CommandOption("--recurse")]
    [Description("Include the transitively referenced projects of the scope.")]
    public bool Recurse { get; init; }

    [CommandOption("--source <CULTURE>")]
    [Description("The source language the in-code defaults are written in (default: en).")]
    [DefaultValue("en")]
    public string Source { get; init; } = "en";

    /// <summary>The <c>--input</c> value, whose meaning (assemblies vs catalogs) each scope names.</summary>
    protected abstract string? InputPath { get; }

    /// <summary>An explicit single assembly (authoring only); null when the scope has no such concept.</summary>
    protected virtual string? AssemblyPath => null;

    /// <summary>The resolved scope.</summary>
    public ScopeOptions ToScope() =>
        new(AssemblyPath, InputPath, ScopeInput.Optional(Project), ScopeInput.Optional(Solution), Recurse);
}

/// <summary>
/// The scope of the authoring commands (<c>status</c>/<c>extract</c>/<c>add</c>/<c>sync</c>): a whole app's built
/// assemblies, defaulting to the project or solution in the current directory. A single explicit <c>--assembly</c>
/// is the low-level path; everything else fans out over a build-output tree.
/// </summary>
internal class AuthoringScopeSettings : ScopeSettings
{
    [CommandOption("--assembly <FILE>")]
    [Description("A single built assembly to read, instead of discovering a scope.")]
    public string? Assembly { get; init; }

    [CommandOption("--input <DIR>")]
    [Description("A directory of built assemblies to read.")]
    public string? Input { get; init; }

    [CommandOption("--no-annotations")]
    [Description("Extract only IL call sites, omitting the [[Localized…]] attribute strings.")]
    public bool NoAnnotations { get; init; }

    [CommandOption("--references")]
    [Description("Record the source files each string is used in (needs a PDB); off by default.")]
    public bool References { get; init; }

    [CommandOption("--catalog-path <PROJECT_SUBPATH>")]
    [Description("The catalog folder inside each project (default: Translations).")]
    public string? CatalogPath { get; init; }

    [CommandOption("--output <DIR>")]
    [Description("Write every catalog into this one directory instead, relative to the current directory; wins over --catalog-path.")]
    public string? Output { get; init; }

    /// <summary>Whether attribute-carried strings are extracted (on unless <c>--no-annotations</c>).</summary>
    public bool IncludeAnnotations => !NoAnnotations;

    /// <summary>Whether source-file references are recorded (off unless <c>--references</c>). Opt-in because a
    /// reference is a convenience for translators, not part of a string's identity, and it ties a git-tracked
    /// catalog to where the code happens to live: moving a call then rewrites the catalog for every language.</summary>
    public bool IncludeReferences => References;

    /// <summary>
    /// The single directory every catalog is written to, or null when each project keeps its own. This is
    /// <c>--output</c>, resolved against the current directory like the dotnet CLI's own <c>--output</c> — MSBuild's
    /// project-relative <c>OutputPath</c> behaviour is <see cref="CatalogPath"/> instead. Set, it wins: the two say
    /// different things and the explicit destination is the more specific instruction.
    /// </summary>
    public string? FlatDirectory => string.IsNullOrEmpty(Output) ? null : Path.GetFullPath(Output);

    /// <summary>The folder inside each project, defaulted.</summary>
    public string CatalogFolder => string.IsNullOrEmpty(CatalogPath) ? CatalogDirectoryResolver.CatalogFolderName : CatalogPath;

    protected override string? InputPath => Input;

    protected override string? AssemblyPath => Assembly;
}

/// <summary>
/// The scope of the handover commands (<c>export</c>/<c>merge</c>/<c>manifest</c>): the catalog directories of an
/// app, defaulting to the Translations folder of the project or solution in the current directory.
/// </summary>
internal class CatalogScopeSettings : ScopeSettings
{
    [CommandOption("--input <DIR>")]
    [Description("A catalog directory to read.")]
    public string? Input { get; init; }

    protected override string? InputPath => Input;
}
