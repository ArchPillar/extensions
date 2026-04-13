namespace ArchPillar.Extensions.Primitives.Tests;

/// <summary>
/// Locks in the allocation-free hot path of <see cref="Pipeline{T}"/>. On the
/// synchronous path (handler returns a cached completed task, middlewares
/// tail-call <c>next</c> without <c>async</c>/<c>await</c>), executing the
/// pipeline must allocate zero bytes.
/// <para>
/// These tests use <see cref="GC.GetAllocatedBytesForCurrentThread"/>, which
/// is precise enough to detect a single heap allocation. If the Pipeline ever
/// starts rebuilding its delegate chain per call, or inserts a state machine
/// on the hot path, these assertions will fail.
/// </para>
/// </summary>
public class PipelineAllocationTests
{
    // Run a decent number of invocations so the assertion tolerates the
    // occasional bookkeeping byte from the runtime but still catches any
    // per-call allocation.
    private const int Invocations = 1_000;

    [Fact]
    public void ExecuteAsync_HandlerOnly_IsAllocationFree()
    {
        var pipeline = new Pipeline<object>(new NoopHandler(), []);
        var context = new object();

        AssertZeroAllocations(() =>
        {
            for (var i = 0; i < Invocations; i++)
            {
                pipeline.ExecuteAsync(context).GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void ExecuteAsync_WithOnePassthroughMiddleware_IsAllocationFree()
    {
        var pipeline = new Pipeline<object>(
            new NoopHandler(),
            [new PassthroughMiddleware()]);
        var context = new object();

        AssertZeroAllocations(() =>
        {
            for (var i = 0; i < Invocations; i++)
            {
                pipeline.ExecuteAsync(context).GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void ExecuteAsync_WithTenPassthroughMiddlewares_IsAllocationFree()
    {
        var middlewares = new IPipelineMiddleware<object>[10];
        for (var i = 0; i < middlewares.Length; i++)
        {
            middlewares[i] = new PassthroughMiddleware();
        }

        var pipeline = new Pipeline<object>(new NoopHandler(), middlewares);
        var context = new object();

        AssertZeroAllocations(() =>
        {
            for (var i = 0; i < Invocations; i++)
            {
                pipeline.ExecuteAsync(context).GetAwaiter().GetResult();
            }
        });
    }

    private static void AssertZeroAllocations(Action action)
    {
        // Warm-up call: triggers JIT compilation so the first iteration does
        // not skew the measurement. GetAllocatedBytesForCurrentThread is a
        // running total per-thread, so we can take a delta around the measured
        // region without having to collect first.
        action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        var after = GC.GetAllocatedBytesForCurrentThread();

        var allocated = after - before;
        Assert.True(
            allocated == 0,
            $"Expected zero allocations on the pipeline hot path, but {allocated} bytes were allocated over {Invocations} invocations.");
    }

    private sealed class NoopHandler : IPipelineHandler<object>
    {
        public Task HandleAsync(object context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class PassthroughMiddleware : IPipelineMiddleware<object>
    {
        public Task InvokeAsync(object context, PipelineDelegate<object> next, CancellationToken cancellationToken = default)
            => next(context, cancellationToken);
    }
}
