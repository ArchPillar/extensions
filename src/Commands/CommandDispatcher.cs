using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands;

// Default <see cref="ICommandDispatcher"/>. Every dispatch goes through the
// shared Pipeline<CommandContext> resolved from DI. Internal because it
// depends on the internal CommandInvokerRegistry; consumers resolve via
// ICommandDispatcher.
internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly Pipeline<CommandContext> _pipeline;
    private readonly CommandInvokerRegistry _registry;
    private readonly IServiceProvider _services;

    public CommandDispatcher(
        Pipeline<CommandContext> pipeline,
        CommandInvokerRegistry registry,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        _pipeline = pipeline;
        _registry = registry;
        _services = services;
    }

    public async Task<OperationResult> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new CommandContext(command);
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result
            ?? OperationResult.Failed(OperationStatus.InternalServerError, "Command pipeline produced no result.");
    }

    public async Task<OperationResult<TResult>> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new CommandContext(command);
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result switch
        {
            OperationResult<TResult> typed => typed,
            OperationResult untyped => Coerce<TResult>(untyped),
            _ => OperationResult<TResult>.Failed(OperationStatus.InternalServerError, "Command pipeline produced no result."),
        };
    }

    public async Task<IReadOnlyList<OperationResult>> SendBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return [];
        }

        CommandInvokerDescriptor descriptor = _registry.Get(typeof(TCommand));

        if (descriptor.InvokeBatchAsync is { } batch)
        {
            // Run validation first per command; pass only the valid ones to the batch handler.
            return await RunBatchAsync(commands, descriptor, batch, cancellationToken).ConfigureAwait(false);
        }

        // Fan out via the regular pipeline to keep middlewares applied.
        OperationResult[] results = new OperationResult[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            results[i] = await SendAsync(commands[i], cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    public async Task<IReadOnlyList<OperationResult<TResult>>> SendBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return [];
        }

        CommandInvokerDescriptor descriptor = _registry.Get(typeof(TCommand));

        if (descriptor.InvokeBatchAsync is { } batch)
        {
            IReadOnlyList<OperationResult> raw = await RunBatchAsync(commands, descriptor, batch, cancellationToken).ConfigureAwait(false);
            OperationResult<TResult>[] typed = new OperationResult<TResult>[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                typed[i] = raw[i] is OperationResult<TResult> t ? t : Coerce<TResult>(raw[i]);
            }

            return typed;
        }

        OperationResult<TResult>[] results = new OperationResult<TResult>[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            results[i] = await SendAsync(commands[i], cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private async Task<IReadOnlyList<OperationResult>> RunBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        CommandInvokerDescriptor descriptor,
        Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<IReadOnlyList<OperationResult>>> batch,
        CancellationToken cancellationToken)
        where TCommand : IRequest
    {
        // Per-command validation up front. Indices that pass are forwarded to the batch handler;
        // indices that fail keep their validation result.
        OperationResult?[] preliminary = new OperationResult?[commands.Count];
        List<TCommand>? valid = null;
        List<int>? validIndices = null;

        for (var i = 0; i < commands.Count; i++)
        {
            TCommand command = commands[i];
            ArgumentNullException.ThrowIfNull(command);

            var validation = new Validation.ValidationContext();
            await descriptor.ValidateAsync(_services, command, validation, cancellationToken).ConfigureAwait(false);

            if (validation.HasErrors)
            {
                preliminary[i] = OperationResult.ValidationFailed(validation.Errors);
            }
            else
            {
                (valid ??= new List<TCommand>(commands.Count)).Add(command);
                (validIndices ??= new List<int>(commands.Count)).Add(i);
            }
        }

        if (valid is { Count: > 0 })
        {
            IRequest[] payload = new IRequest[valid.Count];
            for (var i = 0; i < valid.Count; i++)
            {
                payload[i] = valid[i];
            }

            IReadOnlyList<OperationResult> handled = await batch(_services, payload, cancellationToken).ConfigureAwait(false);
            if (handled.Count != valid.Count)
            {
                throw new InvalidOperationException(
                    $"Batch handler for {typeof(TCommand).FullName} returned {handled.Count} results for {valid.Count} commands.");
            }

            for (var i = 0; i < valid.Count; i++)
            {
                preliminary[validIndices![i]] = handled[i];
            }
        }

        OperationResult[] final = new OperationResult[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            final[i] = preliminary[i]
                ?? OperationResult.Failed(OperationStatus.InternalServerError, "Batch produced no result for command.");
        }

        return final;
    }

    private static OperationResult<TResult> Coerce<TResult>(OperationResult source)
        => new()
        {
            Status = source.Status,
            Errors = source.Errors,
            Exception = source.Exception,
        };
}
