using System.Collections.Concurrent;

namespace ArchPillar.Extensions.Commands.Internal;

/// <summary>
/// Singleton registry of <see cref="CommandInvokerDescriptor"/>s. Population
/// is lazy: descriptors are scanned from the DI-supplied
/// <see cref="IEnumerable{T}"/> only on first lookup of each command type.
/// This keeps host startup costs proportional to the number of commands
/// actually dispatched, not to the number registered.
/// </summary>
internal sealed class CommandInvokerRegistry
{
    private readonly IEnumerable<BatchInvokerEntry> _batches;
    private readonly ConcurrentDictionary<Type, CommandInvokerDescriptor?> _cache = new();

    public CommandInvokerRegistry(
        IEnumerable<CommandInvokerDescriptor> descriptors,
        IEnumerable<BatchInvokerEntry> batches)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batches);
        Descriptors = descriptors;
        _batches = batches;
    }

    public IEnumerable<CommandInvokerDescriptor> Descriptors { get; }

    public bool TryGet(Type commandType, out CommandInvokerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        CommandInvokerDescriptor? resolved = _cache.GetOrAdd(commandType, ResolveDescriptor);
        if (resolved is null)
        {
            descriptor = null!;
            return false;
        }

        descriptor = resolved;
        return true;
    }

    public CommandInvokerDescriptor Get(Type commandType)
    {
        if (!TryGet(commandType, out CommandInvokerDescriptor descriptor))
        {
            throw new InvalidOperationException(
                $"No command handler registered for {commandType.FullName}.");
        }

        return descriptor;
    }

    private CommandInvokerDescriptor? ResolveDescriptor(Type commandType)
    {
        CommandInvokerDescriptor? lastMatch = null;

        // Linear scan; small N in practice, only paid once per command type.
        // Multiple registrations for the same type collapse to the most
        // recently registered descriptor (matches IServiceCollection semantics
        // where the latest registration wins for resolved-by-type lookups).
        foreach (CommandInvokerDescriptor descriptor in Descriptors)
        {
            if (descriptor.CommandType == commandType)
            {
                lastMatch = descriptor;
            }
        }

        if (lastMatch is null)
        {
            return null;
        }

        // Attach the batch leg if one was registered.
        BatchInvokerEntry? batch = null;
        foreach (BatchInvokerEntry entry in _batches)
        {
            if (entry.CommandType == commandType)
            {
                batch = entry;
            }
        }

        if (batch is not null)
        {
            return new CommandInvokerDescriptor(
                lastMatch.CommandType,
                lastMatch.ValidateAsync,
                lastMatch.InvokeAsync,
                lastMatch.ResolveHandler,
                batch.InvokeBatchAsync);
        }

        return lastMatch;
    }
}
