using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Commands.Middlewares;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering the command
/// dispatcher, handlers, and the optional batch handlers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the command dispatcher and its shared pipeline. Built-in
    /// middlewares (telemetry, exception, validation) are added in the order
    /// the dispatcher expects.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCommands(
        this IServiceCollection services,
        Action<CommandsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CommandsOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<CommandInvokerRegistry>();
        services.TryAddScoped<ICommandDispatcher, CommandDispatcher>();

        // Shared pipeline: terminal handler is the router. The router runs
        // both validation and the handler, so user-added middlewares
        // (transactions, unit-of-work, retry, …) wrap both consistently.
        // Middlewares are contributed via TryAddEnumerable so duplicate
        // registrations are no-ops.
        services.AddPipeline<CommandContext, CommandRouter>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipelineMiddleware<CommandContext>, ActivityMiddleware<CommandContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipelineMiddleware<CommandContext>, ExceptionMiddleware>());

        return services;
    }

    /// <summary>
    /// Registers a command handler for a fire-and-forget command.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for <typeparamref name="THandler"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCommandHandler<TCommand, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCommand : class, ICommand
        where THandler : class, ICommandHandler<TCommand>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(ServiceDescriptor.Describe(
            typeof(ICommandHandler<TCommand>),
            typeof(THandler),
            lifetime));

        services.AddSingleton(new CommandInvokerDescriptor(
            typeof(TCommand),
            ValidateAsync,
            InvokeAsync));

        return services;

        static async Task ValidateAsync(IServiceProvider sp, IRequest command, Validation.IValidationContext ctx, CancellationToken ct)
        {
            ICommandHandler<TCommand> handler = sp.GetRequiredService<ICommandHandler<TCommand>>();
            await handler.ValidateAsync((TCommand)command, ctx, ct).ConfigureAwait(false);
        }

        static async Task<OperationResult> InvokeAsync(IServiceProvider sp, IRequest command, CancellationToken ct)
        {
            ICommandHandler<TCommand> handler = sp.GetRequiredService<ICommandHandler<TCommand>>();
            return await handler.HandleAsync((TCommand)command, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Registers a command handler for a result-bearing command.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The payload type returned on success.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for <typeparamref name="THandler"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCommand : class, ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(ServiceDescriptor.Describe(
            typeof(ICommandHandler<TCommand, TResult>),
            typeof(THandler),
            lifetime));

        services.AddSingleton(new CommandInvokerDescriptor(
            typeof(TCommand),
            ValidateAsync,
            InvokeAsync));

        return services;

        static async Task ValidateAsync(IServiceProvider sp, IRequest command, Validation.IValidationContext ctx, CancellationToken ct)
        {
            ICommandHandler<TCommand, TResult> handler = sp.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            await handler.ValidateAsync((TCommand)command, ctx, ct).ConfigureAwait(false);
        }

        static async Task<OperationResult> InvokeAsync(IServiceProvider sp, IRequest command, CancellationToken ct)
        {
            ICommandHandler<TCommand, TResult> handler = sp.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            return await handler.HandleAsync((TCommand)command, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Registers an optional batch handler for <typeparamref name="TCommand"/>.
    /// Requires a single-command handler to also be registered for the same
    /// command type — the batch path validates per command and forwards
    /// only the valid ones to <see cref="IBatchCommandHandler{TCommand}"/>.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="THandler">The batch handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for <typeparamref name="THandler"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddBatchCommandHandler<TCommand, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCommand : class, ICommand
        where THandler : class, IBatchCommandHandler<TCommand>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(ServiceDescriptor.Describe(
            typeof(IBatchCommandHandler<TCommand>),
            typeof(THandler),
            lifetime));

        services.AddSingleton(new BatchInvokerEntry(typeof(TCommand), InvokeBatchAsync));

        return services;

        static async Task<IReadOnlyList<OperationResult>> InvokeBatchAsync(
            IServiceProvider sp,
            IReadOnlyList<IRequest> commands,
            CancellationToken ct)
        {
            IBatchCommandHandler<TCommand> handler = sp.GetRequiredService<IBatchCommandHandler<TCommand>>();
            TCommand[] typed = new TCommand[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                typed[i] = (TCommand)commands[i];
            }

            return await handler.HandleBatchAsync(typed, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Registers an optional batch handler for a result-bearing command.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The payload type returned on success.</typeparam>
    /// <typeparam name="THandler">The batch handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for <typeparamref name="THandler"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddBatchCommandHandler<TCommand, TResult, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCommand : class, ICommand<TResult>
        where THandler : class, IBatchCommandHandler<TCommand, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(ServiceDescriptor.Describe(
            typeof(IBatchCommandHandler<TCommand, TResult>),
            typeof(THandler),
            lifetime));

        services.AddSingleton(new BatchInvokerEntry(typeof(TCommand), InvokeBatchAsync));

        return services;

        static async Task<IReadOnlyList<OperationResult>> InvokeBatchAsync(
            IServiceProvider sp,
            IReadOnlyList<IRequest> commands,
            CancellationToken ct)
        {
            IBatchCommandHandler<TCommand, TResult> handler = sp.GetRequiredService<IBatchCommandHandler<TCommand, TResult>>();
            TCommand[] typed = new TCommand[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                typed[i] = (TCommand)commands[i];
            }

            IReadOnlyList<OperationResult<TResult>> handled = await handler.HandleBatchAsync(typed, ct).ConfigureAwait(false);
            OperationResult[] erased = new OperationResult[handled.Count];
            for (var i = 0; i < handled.Count; i++)
            {
                erased[i] = handled[i];
            }

            return erased;
        }
    }

    /// <summary>
    /// Forces eager resolution of every registered command descriptor. Call
    /// this once after building the service provider when
    /// <see cref="CommandsOptions.ValidateHandlersAtStartup"/> is enabled (or
    /// any time you want missing-handler errors to surface up front).
    /// </summary>
    /// <param name="services">A service provider built from the configured collection.</param>
    /// <returns>The service provider, for chaining.</returns>
    public static IServiceProvider ValidateCommandRegistrations(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetRequiredService<CommandInvokerRegistry>().ValidateAll();
        return services;
    }
}
