using System.Diagnostics;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// The pipeline context flowing through <see cref="ICommandDispatcher"/>.
/// Carries the command being dispatched, an output slot for the
/// <see cref="OperationResult"/>, and a <see cref="ValidationContext"/>
/// populated by the router when it calls the handler's
/// <c>ValidateAsync</c> ahead of <c>HandleAsync</c>.
/// </summary>
/// <remarks>
/// The pipeline is shared across all command types — middlewares see this
/// untyped context and can branch on <see cref="CommandType"/> when they need
/// per-command behavior.
/// </remarks>
public sealed class CommandContext : IPipelineContext
{
    /// <summary>
    /// Initializes a new <see cref="CommandContext"/> for a single dispatch.
    /// </summary>
    /// <param name="command">The command being dispatched.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="command"/> is <c>null</c>.</exception>
    public CommandContext(IRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Command = command;
        CommandType = command.GetType();
        Validation = new ValidationContext();
    }

    /// <summary>
    /// Initializes a new <see cref="CommandContext"/> for a batch dispatch.
    /// The pipeline runs once around the whole batch — middlewares
    /// (transactions, telemetry, exception) wrap the entire group rather
    /// than each item.
    /// </summary>
    /// <param name="commands">The commands to dispatch as a batch. Must contain at least one element.</param>
    /// <param name="commandType">
    /// The runtime element type of the batch. Surfaced via
    /// <see cref="CommandType"/> so middlewares can branch on it the same
    /// way they would for a single dispatch.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="commands"/> or <paramref name="commandType"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="commands"/> is empty.</exception>
    public CommandContext(IReadOnlyList<IRequest> commands, Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(commandType);
        if (commands.Count == 0)
        {
            throw new ArgumentException("Batch must contain at least one command.", nameof(commands));
        }

        BatchCommands = commands;
        CommandType = commandType;
        Validation = new ValidationContext();
    }

    /// <summary>
    /// The command being dispatched in single mode; <c>null</c> when this
    /// context represents a batch (see <see cref="BatchCommands"/>).
    /// </summary>
    public IRequest? Command { get; }

    /// <summary>
    /// The commands being dispatched as a batch; <c>null</c> in single mode.
    /// Middlewares can use <see cref="IsBatch"/> to branch.
    /// </summary>
    public IReadOnlyList<IRequest>? BatchCommands { get; }

    /// <summary><c>true</c> when this context represents a batch dispatch.</summary>
    public bool IsBatch => BatchCommands is not null;

    /// <summary>The runtime type of the dispatched command (the element type when batch).</summary>
    public Type CommandType { get; }

    /// <summary>
    /// The accumulated validation context. Populated when the router invokes
    /// the handler's <c>ValidateAsync</c> immediately before dispatch. Unused
    /// in batch mode — per-item validation accumulators are tracked
    /// internally by the router.
    /// </summary>
    public IValidationContext Validation { get; }

    /// <summary>
    /// The outcome of the dispatch. Populated by the router (single mode) or
    /// the exception middleware. In batch mode this slot carries the
    /// aggregate result — success when every item succeeded, otherwise a
    /// failure summarising the batch (per-item details live on
    /// <see cref="BatchResults"/>). The dispatcher reads this slot after
    /// <see cref="Pipeline{T}.ExecuteAsync"/> completes so user middlewares
    /// (transaction commit/rollback, retry) can branch on the overall outcome.
    /// </summary>
    public OperationResult? Result { get; set; }

    /// <summary>
    /// Per-item outcomes for a batch dispatch, aligned 1:1 with
    /// <see cref="BatchCommands"/>. <c>null</c> in single mode.
    /// </summary>
    public IReadOnlyList<OperationResult>? BatchResults { get; set; }

    /// <inheritdoc/>
    public string OperationName => "Commands." + CommandType.Name;

    /// <inheritdoc/>
    public ActivityKind ActivityKind => ActivityKind.Internal;

    /// <inheritdoc/>
    public ActivityContext ParentContext => default;

    /// <inheritdoc/>
    public void EnrichActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        activity.SetTag("command.type", CommandType.FullName);
        if (BatchCommands is not null)
        {
            activity.SetTag("command.batch.size", BatchCommands.Count);
        }
    }
}
