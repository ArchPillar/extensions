using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Pipelines;
using Command.WebApiSample.Notes;
using Microsoft.EntityFrameworkCore.Storage;

namespace Command.WebApiSample.Infrastructure;

/// <summary>
/// Wraps every command dispatch in a database transaction. Commits when the
/// command produces a successful <c>OperationResult</c>; rolls back on any
/// failure or thrown exception. Validation runs inside the router, so the
/// transactional snapshot is in scope for both validation reads and the
/// handler's writes — eliminating the TOCTOU window between them.
/// </summary>
internal sealed class TransactionMiddleware(NotesDbContext context, ILogger<TransactionMiddleware> logger)
    : IPipelineMiddleware<CommandContext>
{
    public async Task InvokeAsync(
        CommandContext commandContext,
        PipelineDelegate<CommandContext> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandContext);
        ArgumentNullException.ThrowIfNull(next);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        await next(commandContext, cancellationToken);

        if (commandContext.Result is { IsSuccess: true })
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "Rolling back transaction for {CommandType}: status={Status}",
                commandContext.CommandType.Name,
                commandContext.Result?.Status);
            await transaction.RollbackAsync(cancellationToken);
        }
    }
}
