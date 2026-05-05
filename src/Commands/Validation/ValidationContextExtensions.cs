using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Builds <see cref="OperationResult"/> / <see cref="OperationProblem"/>
/// values from an <see cref="IValidationContext"/>'s accumulated entries.
/// </summary>
public static class ValidationContextExtensions
{
    /// <summary>
    /// Folds the accumulated entries into an <see cref="OperationResult"/>:
    /// field-bearing errors are grouped into <see cref="OperationProblem.Errors"/>,
    /// the result <see cref="OperationResult.Status"/> follows the precedence
    /// <c>401 &gt; 403 &gt; 404 &gt; 409 &gt; 412 &gt; 400 &gt; 422</c>, and the
    /// highest-precedence top-level error's title/detail/extensions
    /// promote onto <see cref="OperationProblem.Title"/> /
    /// <see cref="OperationProblem.Detail"/> /
    /// <see cref="OperationProblem.Extensions"/>.
    /// </summary>
    /// <param name="context">The validation context with accumulated entries.</param>
    /// <returns>
    /// An <see cref="OperationResult"/> describing the failure, or <c>null</c>
    /// when the context has no errors.
    /// </returns>
    public static OperationResult? ToFailureResult(this IValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ValidationEntry> entries = context.Entries;
        if (entries.Count == 0)
        {
            return null;
        }

        OperationStatus status = OperationStatus.BadRequest;
        ValidationEntry? topLevelDriver = null;

        foreach (ValidationEntry entry in entries)
        {
            OperationStatus entryStatus = entry.Error.Status;
            if (PrecedenceRank(entryStatus) > PrecedenceRank(status))
            {
                status = entryStatus;
            }

            if (entry.Field is null && (topLevelDriver is null
                || PrecedenceRank(entry.Error.Status) > PrecedenceRank(topLevelDriver.Value.Error.Status)))
            {
                topLevelDriver = entry;
            }
        }

        Dictionary<string, List<OperationError>>? grouped = null;
        foreach (ValidationEntry entry in entries)
        {
            if (entry.Field is null)
            {
                continue;
            }

            grouped ??= [];
            if (!grouped.TryGetValue(entry.Field, out List<OperationError>? bucket))
            {
                bucket = [];
                grouped[entry.Field] = bucket;
            }

            bucket.Add(entry.Error);
        }

        Dictionary<string, IReadOnlyList<OperationError>>? errorsDict = null;
        if (grouped is not null)
        {
            errorsDict = new Dictionary<string, IReadOnlyList<OperationError>>(grouped.Count);
            foreach (KeyValuePair<string, List<OperationError>> kvp in grouped)
            {
                errorsDict[kvp.Key] = kvp.Value;
            }
        }

        var problem = new OperationProblem
        {
            Type = topLevelDriver?.Error.Type
                ?? (errorsDict is null ? status.Type() : "validation"),
            Title = status.Title(),
            Detail = topLevelDriver?.Error.Detail
                ?? (errorsDict is null ? null : "One or more validation errors occurred."),
            Errors = errorsDict,
            Extensions = topLevelDriver?.Error.Extensions,
        };

        return new OperationResult { Status = status, Problem = problem };
    }

    private static int PrecedenceRank(OperationStatus status)
        => status switch
        {
            OperationStatus.Unauthorized => 7,
            OperationStatus.Forbidden => 6,
            OperationStatus.NotFound => 5,
            OperationStatus.Conflict => 4,
            OperationStatus.PreconditionFailed => 3,
            OperationStatus.BadRequest => 2,
            OperationStatus.UnprocessableEntity => 1,
            _ => 0,
        };
}
