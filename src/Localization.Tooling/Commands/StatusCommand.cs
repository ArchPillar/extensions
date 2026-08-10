using System.ComponentModel;
using System.Globalization;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// Reports how much of the app is translated. The measurement is always the same — every project/language pair
/// against its project's extracted template — and <c>--detail</c> only chooses how far it is aggregated before
/// being shown: the whole app, per language, per project, or the full matrix.
/// </summary>
internal sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    /// <summary>Options for <c>status</c>.</summary>
    internal sealed class Settings : AuthoringScopeSettings
    {
        [CommandOption("--detail <LEVEL>")]
        [Description("How far to aggregate: overall (default), language, project, or matrix.")]
        public string? Detail { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        StatusDetail detail;
        try
        {
            detail = ParseDetail(settings.Detail);
        }
        catch (ArgumentException error)
        {
            return ToolConsole.Fail(error.Message);
        }

        var sourceLanguage = settings.Source;
        var rows = new List<TranslationProgressRow>();
        // Every project with strings, including those no language has been added for yet — they have no row in the
        // matrix but still belong in the report, and are the whole answer before any translation starts.
        var stringsByProject = new Dictionary<string, int>(StringComparer.Ordinal);
        await ScopeRunner.ForEachTemplateAsync(settings, "Scanning", (name, catalogDirectory, template) =>
        {
            stringsByProject[name] = template.Entries.Count;
            if (Directory.Exists(catalogDirectory))
            {
                foreach (var path in CatalogNaming.TargetCatalogsFor(catalogDirectory, name, sourceLanguage))
                {
                    Catalog catalog = CatalogIo.ReadFile(CatalogIo.ProviderFor(path), path);
                    rows.Add(new TranslationProgressRow(
                        name,
                        CatalogNaming.CultureOf(path),
                        TranslationProgress.Measure(catalog, template.Entries.Count)));
                }
            }

            return Task.CompletedTask;
        });

        if (stringsByProject.Count == 0)
        {
            ToolConsole.Info("No assemblies with localizable strings found in the given scope. Build first, then point --input/--project/--solution at the output.");
            return 0;
        }

        if (rows.Count == 0)
        {
            // Nothing to measure yet: report what there is to translate, which is also how `status` answers
            // "which assemblies have strings?" before any language exists.
            WriteStringsOnly(stringsByProject);
        }
        else
        {
            AnsiConsole.Write(Render(detail, rows, stringsByProject));
        }

        var languages = rows.Select(row => row.Culture).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        ToolConsole.Info(
            $"{stringsByProject.Count} assembly(ies), {stringsByProject.Values.Sum()} string(s), {languages} language(s), source {sourceLanguage}.");
        return 0;
    }

    // The default is the headline number — "how much of the app is translated?" — which is the question status is
    // usually asked. The narrower levels say where the remainder is.
    private static StatusDetail ParseDetail(string? detail) => detail?.ToLowerInvariant() switch
    {
        null or "" or "overall" => StatusDetail.Overall,
        "language" => StatusDetail.Language,
        "project" => StatusDetail.Project,
        "matrix" => StatusDetail.Matrix,
        _ => throw new ArgumentException($"Unknown detail level '{detail}'. Use overall, language, project, or matrix.")
    };

    private static Table Render(StatusDetail detail, List<TranslationProgressRow> rows, Dictionary<string, int> stringsByProject) => detail switch
    {
        StatusDetail.Language => ByLanguage(rows),
        StatusDetail.Project => ByProject(rows, stringsByProject),
        StatusDetail.Matrix => Matrix(rows),
        _ => Overall(rows, stringsByProject)
    };

    // One line for the whole app. The total is translation *units* — every string once per language — since that
    // is the work the percentage measures.
    private static Table Overall(List<TranslationProgressRow> rows, Dictionary<string, int> stringsByProject)
    {
        var languages = rows.Select(row => row.Culture).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Table table = NewTable("Units", "Projects", "Languages");
        AddRow(
            table,
            Total(rows),
            new Text(stringsByProject.Count.ToString(CultureInfo.InvariantCulture)),
            new Text(languages.ToString(CultureInfo.InvariantCulture)));
        return table;
    }

    private static Table ByLanguage(List<TranslationProgressRow> rows)
    {
        Table table = NewTable("Strings", "Language");
        foreach (IGrouping<string, TranslationProgressRow> group in rows
            .GroupBy(row => row.Culture, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddRow(table, Total(group), new Text(group.Key));
        }

        return table;
    }

    // Per project, aggregated over its languages, so the total is units again — and the language count is shown so
    // the multiplication behind it is visible. A project no language has been added for shows its strings and no
    // coverage, which is honest: it is not 0% translated, it is not being translated.
    private static Table ByProject(List<TranslationProgressRow> rows, Dictionary<string, int> stringsByProject)
    {
        Dictionary<string, List<TranslationProgressRow>> byProject = rows
            .GroupBy(row => row.Project, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        Table table = NewTable("Units", "Project", "Strings", "Languages");
        table.Columns[1].RightAligned();
        table.Columns[2].RightAligned();
        foreach ((var project, var strings) in stringsByProject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var stringCount = new Text(strings.ToString(CultureInfo.InvariantCulture));
            if (!byProject.TryGetValue(project, out List<TranslationProgressRow>? projectRows))
            {
                // No language added yet: there is nothing to measure, and 0% would misreport "not started" as
                // "translated none of it".
                table.AddRow(new Text(project), stringCount, new Text("0"), new Text("—"), new Text("—"), new Text("—"), new Text("—"), new Text("—"));
                continue;
            }

            AddRow(
                table,
                Total(projectRows),
                new Text(project),
                stringCount,
                new Text(projectRows.Count.ToString(CultureInfo.InvariantCulture)));
        }

        return table;
    }

    private static Table Matrix(List<TranslationProgressRow> rows)
    {
        Table table = NewTable("Strings", "Project", "Language");
        foreach (TranslationProgressRow row in rows
            .OrderBy(row => row.Project, StringComparer.Ordinal)
            .ThenBy(row => row.Culture, StringComparer.OrdinalIgnoreCase))
        {
            AddRow(table, row.Progress, new Text(row.Project), new Text(row.Culture));
        }

        return table;
    }

    // Every level shares the same trailing measurement columns — the total the level measures against, then the
    // breakdown — and only the leading scope columns differ. AddRow fills them in the same order.
    private static Table NewTable(string totalLabel, params string[] scopeColumns)
    {
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var column in scopeColumns)
        {
            table.AddColumn(new TableColumn(column));
        }

        foreach (var column in new[] { totalLabel, "Translated", "Review", "Missing", "%" })
        {
            table.AddColumn(new TableColumn(column).RightAligned());
        }

        return table;
    }

    private static void AddRow(Table table, TranslationProgress progress, params IRenderable[] scope)
    {
        var cells = new List<IRenderable>(scope)
        {
            new Text(progress.Total.ToString(CultureInfo.InvariantCulture)),
            new Text(progress.Translated.ToString(CultureInfo.InvariantCulture)),
            new Text(progress.NeedsReview.ToString(CultureInfo.InvariantCulture)),
            new Text(progress.Missing.ToString(CultureInfo.InvariantCulture)),
            new Text(Percent(progress))
        };
        table.AddRow(cells);
    }

    private static TranslationProgress Total(IEnumerable<TranslationProgressRow> rows) =>
        rows.Aggregate(default(TranslationProgress), (running, row) => running + row.Progress);

    // Whole percent, floored, so a run that is one string short of complete never reads as 100%.
    private static string Percent(TranslationProgress progress) =>
        progress.Total == 0
            ? "—"
            : ((int)Math.Floor(progress.Fraction * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    // Before any language exists there is nothing to measure, so the report is simply what there is to translate.
    private static void WriteStringsOnly(Dictionary<string, int> stringsByProject)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Project");
        table.AddColumn(new TableColumn("Strings").RightAligned());
        foreach ((var project, var strings) in stringsByProject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            // Text, not the string overload: a cell is parsed as markup, so a project named e.g. "App[1]" would
            // throw on render. Text renders it literally.
            table.AddRow(new Text(project), new Text(strings.ToString(CultureInfo.InvariantCulture)));
        }

        AnsiConsole.Write(table);
    }
}
