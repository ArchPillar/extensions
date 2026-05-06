using System.Diagnostics.CodeAnalysis;
using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Commands.Middlewares;
using ArchPillar.Extensions.Operations;
using ArchPillar.Extensions.Pipelines;
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
    /// middlewares (telemetry, exception) are added in the order the
    /// dispatcher expects.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CommandInvokerRegistry>();
        services.TryAddScoped<ICommandDispatcher, CommandDispatcher>();

        // Shared pipeline: terminal handler is the router. The router runs
        // both validation and the handler, so user-added middlewares
        // (transactions, unit-of-work, retry, …) wrap both consistently.
        // Middlewares are contributed via TryAddEnumerable so duplicate
        // registrations are no-ops.
        services.AddPipeline<CommandContext, CommandRouter>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipelineMiddleware<CommandContext>, CommandActivityMiddleware>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipelineMiddleware<CommandContext>, ExceptionMiddleware>());

        return services;
    }

    /// <summary>
    /// Registers a command handler for a no-result command.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for <typeparamref name="THandler"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCommandHandler<
        TCommand,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
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
            InvokeAsync,
            ResolveHandler));

        return services;

        static async Task ValidateAsync(IServiceProvider services, IRequest command, Validation.IValidationContext validation, CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand> handler = services.GetRequiredService<ICommandHandler<TCommand>>();
            await handler.ValidateAsync((TCommand)command, validation, cancellationToken).ConfigureAwait(false);
        }

        static async Task<OperationResult> InvokeAsync(IServiceProvider services, IRequest command, CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand> handler = services.GetRequiredService<ICommandHandler<TCommand>>();
            return await handler.HandleAsync((TCommand)command, cancellationToken).ConfigureAwait(false);
        }

        static void ResolveHandler(IServiceProvider services)
        {
            services.GetRequiredService<ICommandHandler<TCommand>>();
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
    public static IServiceCollection AddCommandHandler<
        TCommand,
        TResult,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
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
            InvokeAsync,
            ResolveHandler));

        return services;

        static async Task ValidateAsync(IServiceProvider services, IRequest command, Validation.IValidationContext validation, CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand, TResult> handler = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            await handler.ValidateAsync((TCommand)command, validation, cancellationToken).ConfigureAwait(false);
        }

        static async Task<OperationResult> InvokeAsync(IServiceProvider services, IRequest command, CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand, TResult> handler = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            return await handler.HandleAsync((TCommand)command, cancellationToken).ConfigureAwait(false);
        }

        static void ResolveHandler(IServiceProvider services)
        {
            services.GetRequiredService<ICommandHandler<TCommand, TResult>>();
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
    public static IServiceCollection AddBatchCommandHandler<
        TCommand,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
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
            IServiceProvider services,
            IReadOnlyList<IRequest> commands,
            CancellationToken cancellationToken)
        {
            IBatchCommandHandler<TCommand> handler = services.GetRequiredService<IBatchCommandHandler<TCommand>>();
            var typed = new TCommand[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                typed[i] = (TCommand)commands[i];
            }

            return await handler.HandleBatchAsync(typed, cancellationToken).ConfigureAwait(false);
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
    public static IServiceCollection AddBatchCommandHandler<
        TCommand,
        TResult,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
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
            IServiceProvider services,
            IReadOnlyList<IRequest> commands,
            CancellationToken cancellationToken)
        {
            IBatchCommandHandler<TCommand, TResult> handler = services.GetRequiredService<IBatchCommandHandler<TCommand, TResult>>();
            var typed = new TCommand[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                typed[i] = (TCommand)commands[i];
            }

            IReadOnlyList<OperationResult<TResult>> handled = await handler.HandleBatchAsync(typed, cancellationToken).ConfigureAwait(false);
            var erased = new OperationResult[handled.Count];
            for (var i = 0; i < handled.Count; i++)
            {
                erased[i] = handled[i];
            }

            return erased;
        }
    }

    /// <summary>
    /// Resolves every registered command handler from a freshly-created scope
    /// to verify constructor dependencies are satisfied. Throws on the first
    /// handler that fails to resolve, with the offending command type in the
    /// message.
    /// </summary>
    /// <remarks>
    /// Call this once after building the service provider when you want
    /// missing-handler errors to surface up front rather than at first
    /// dispatch. Off by default — host startup stays proportional to commands
    /// actually invoked.
    /// </remarks>
    /// <param name="services">A service provider built from the configured collection.</param>
    /// <returns>The service provider, for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when any handler fails to resolve.</exception>
    public static IServiceProvider ValidateCommandRegistrations(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        CommandInvokerRegistry registry = services.GetRequiredService<CommandInvokerRegistry>();
        using IServiceScope scope = services.CreateScope();

        foreach (CommandInvokerDescriptor descriptor in registry.Descriptors)
        {
            try
            {
                descriptor.ResolveHandler(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot resolve handler for command '{descriptor.CommandType.FullName}'. "
                    + $"See inner exception for the missing dependency.",
                    ex);
            }
        }

        return services;
    }
}
