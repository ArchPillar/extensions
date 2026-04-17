namespace ArchPillar.Extensions.Pipelines.Tests;

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
[Collection(PipelineActivitySourceCollection.Name)]
public sealed class PipelineAllocationTests
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

    // -----------------------------------------------------------------------
    // ActivityMiddleware<T> pass-through (no listener subscribed)
    //
    // Locks in the allocation-free fast path through ActivityMiddleware<T>
    // when no ActivityListener is attached to PipelineActivitySource. The
    // middleware must detect the null activity and tail-call next(...) without
    // producing an async state machine or any other per-call allocation.
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivityMiddleware_WithNoListenerSubscribed_IsAllocationFree()
    {
        // Sanity: any pre-existing listener for the pipeline source would
        // invalidate the "no listener" precondition of this test.
        Assert.False(
            PipelineActivitySource.Instance.HasListeners(),
            "Expected no ActivityListener subscribed to PipelineActivitySource for this test.");

        var pipeline = new Pipeline<TracedContext>(
            new NoopTracedHandler(),
            [new ActivityMiddleware<TracedContext>()]);
        var context = new TracedContext();

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

    private sealed class TracedContext : IPipelineContext
    {
        public string OperationName => "Allocation.Test";
    }

    private sealed class NoopTracedHandler : IPipelineHandler<TracedContext>
    {
        public Task HandleAsync(TracedContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
