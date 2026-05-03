using System.Diagnostics;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// The pipeline context flowing through <see cref="ICommandDispatcher"/>.
/// Carries the command being dispatched, an output slot for the
/// <see cref="OperationResult"/>, and a <see cref="ValidationContext"/>
/// shared between the validation middleware and the handler.
/// </summary>
/// <remarks>
/// The pipeline is shared across all command types — middlewares see this
/// untyped context and can branch on <see cref="CommandType"/> when they need
/// per-command behavior.
/// </remarks>
public sealed class CommandContext : IPipelineContext
{
    /// <summary>
    /// Initializes a new <see cref="CommandContext"/> for <paramref name="command"/>.
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

    /// <summary>The command being dispatched.</summary>
    public IRequest Command { get; }

    /// <summary>The runtime type of <see cref="Command"/>.</summary>
    public Type CommandType { get; }

    /// <summary>
    /// The accumulated validation context. Populated by the handler's
    /// <c>ValidateAsync</c> via the validation middleware.
    /// </summary>
    public IValidationContext Validation { get; }

    /// <summary>
    /// The outcome of the dispatch. Populated by middleware (validation,
    /// exception) or by the terminal router invoking the handler. The
    /// dispatcher reads this slot after <see cref="Pipeline{T}.ExecuteAsync"/>
    /// completes.
    /// </summary>
    public OperationResult? Result { get; set; }

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
    }
}
