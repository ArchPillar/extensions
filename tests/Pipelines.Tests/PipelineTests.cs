namespace ArchPillar.Extensions.Pipelines.Tests;

public class PipelineTests
{
    // -----------------------------------------------------------------------
    // Order of execution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RunsMiddlewaresInRegistrationOrderAsync()
    {
        var log = new List<string>();

        Pipeline<List<string>> pipeline = Pipeline
            .For<List<string>>()
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("m1:before");
                await next(ctx, ct);
                ctx.Add("m1:after");
            })
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("m2:before");
                await next(ctx, ct);
                ctx.Add("m2:after");
            })
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("m3:before");
                await next(ctx, ct);
                ctx.Add("m3:after");
            })
            .Handle(ctx => ctx.Add("handler"))
            .Build();

        await pipeline.ExecuteAsync(log);

        Assert.Equal(
            new[]
            {
                "m1:before",
                "m2:before",
                "m3:before",
                "handler",
                "m3:after",
                "m2:after",
                "m1:after",
            },
            log);
    }

    // -----------------------------------------------------------------------
    // Short-circuit
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MiddlewareThatSkipsNext_SkipsHandlerAndLaterMiddlewaresAsync()
    {
        var log = new List<string>();

        Pipeline<List<string>> pipeline = Pipeline
            .For<List<string>>()
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("m1:before");
                await next(ctx, ct);
                ctx.Add("m1:after");
            })
            .Use((ctx, _, _) =>
            {
                ctx.Add("m2:short-circuit");
                return Task.CompletedTask;
            })
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("m3:never");
                await next(ctx, ct);
            })
            .Handle(ctx => ctx.Add("handler:never"))
            .Build();

        await pipeline.ExecuteAsync(log);

        Assert.Equal(
            new[]
            {
                "m1:before",
                "m2:short-circuit",
                "m1:after",
            },
            log);
    }

    [Fact]
    public async Task ExecuteAsync_FirstMiddlewareShortCircuits_HandlerNotInvokedAsync()
    {
        var handlerInvoked = false;

        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Use((_, _, _) => Task.CompletedTask)
            .Handle(_ => handlerInvoked = true)
            .Build();

        await pipeline.ExecuteAsync(new object());

        Assert.False(handlerInvoked);
    }

    // -----------------------------------------------------------------------
    // Handler-only
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoMiddlewares_InvokesHandlerAsync()
    {
        var handled = false;

        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Handle(_ => handled = true)
            .Build();

        await pipeline.ExecuteAsync(new object());

        Assert.True(handled);
        Assert.Equal(0, pipeline.MiddlewareCount);
    }

    // -----------------------------------------------------------------------
    // Exceptions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_HandlerThrows_ExceptionPropagatesAsync()
    {
        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Handle((_, _) => Task.FromException(new InvalidOperationException("boom")))
            .Build();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteAsync(new object()));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_MiddlewareCatchesHandlerException_ExecutionContinuesAsync()
    {
        var log = new List<string>();

        Pipeline<List<string>> pipeline = Pipeline
            .For<List<string>>()
            .Use(async (ctx, next, ct) =>
            {
                try
                {
                    await next(ctx, ct);
                }
                catch (InvalidOperationException ex)
                {
                    ctx.Add($"caught:{ex.Message}");
                }
            })
            .Handle((_, _) => Task.FromException(new InvalidOperationException("boom")))
            .Build();

        await pipeline.ExecuteAsync(log);

        Assert.Equal(new[] { "caught:boom" }, log);
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancellationToken_FlowsToMiddlewareAndHandlerAsync()
    {
        using var cts = new CancellationTokenSource();

        CancellationToken seenByMiddleware = default;
        CancellationToken seenByHandler = default;

        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Use(async (ctx, next, ct) =>
            {
                seenByMiddleware = ct;
                await next(ctx, ct);
            })
            .Handle((_, ct) =>
            {
                seenByHandler = ct;
                return Task.CompletedTask;
            })
            .Build();

        await pipeline.ExecuteAsync(new object(), cts.Token);

        Assert.Equal(cts.Token, seenByMiddleware);
        Assert.Equal(cts.Token, seenByHandler);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationTokenTriggeredBetweenSteps_PropagatesOperationCanceledAsync()
    {
        using var cts = new CancellationTokenSource();

        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Use(async (ctx, next, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                await next(ctx, ct);
            })
            .Handle(_ => { })
            .Build();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pipeline.ExecuteAsync(new object(), cts.Token));
    }

    // -----------------------------------------------------------------------
    // Reuse and concurrent execution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ReusableAcrossInvocations_IsolatesContextsAsync()
    {
        Pipeline<List<string>> pipeline = Pipeline
            .For<List<string>>()
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("wrap");
                await next(ctx, ct);
            })
            .Handle(ctx => ctx.Add("handler"))
            .Build();

        var firstContext = new List<string>();
        var secondContext = new List<string>();

        await pipeline.ExecuteAsync(firstContext);
        await pipeline.ExecuteAsync(secondContext);

        Assert.Equal(new[] { "wrap", "handler" }, firstContext);
        Assert.Equal(new[] { "wrap", "handler" }, secondContext);
    }

    [Fact]
    public async Task ExecuteAsync_PreBuiltChain_HandlerInstanceIdentityPreservedAcrossInvocationsAsync()
    {
        // Uses a stateful handler that counts invocations on the same instance.
        // If the chain were rebuilt per call, a fresh captured instance could
        // be used each time; this test locks in that the original handler is
        // reused.
        var handler = new CountingHandler();

        var pipeline = new Pipeline<object>(
            handler,
            middlewares: []);

        await pipeline.ExecuteAsync(new object());
        await pipeline.ExecuteAsync(new object());
        await pipeline.ExecuteAsync(new object());

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentInvocations_DoNotInterfereWhenCollaboratorsAreSafeAsync()
    {
        var counter = 0;

        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Use(async (ctx, next, ct) =>
            {
                Interlocked.Increment(ref counter);
                await next(ctx, ct);
            })
            .Handle(_ => Interlocked.Increment(ref counter))
            .Build();

        Task[] tasks = Enumerable.Range(0, 100)
            .Select(_ => pipeline.ExecuteAsync(new object()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(200, counter);
    }

    // -----------------------------------------------------------------------
    // Constructor argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Pipeline<object>(null!, []));
    }

    [Fact]
    public void Constructor_NullMiddlewaresEnumerable_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Pipeline<object>(PipelineHandler.FromDelegate<object>(_ => { }), null!));
    }

    [Fact]
    public void Constructor_MiddlewareEntryIsNull_Throws()
    {
        IPipelineHandler<object> handler = PipelineHandler.FromDelegate<object>(_ => { });
        IPipelineMiddleware<object>[] middlewares = [null!];

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new Pipeline<object>(handler, middlewares));
        Assert.Equal("middlewares", ex.ParamName);
    }

    [Fact]
    public void Constructor_StoresMiddlewareCount()
    {
        IPipelineHandler<object> handler = PipelineHandler.FromDelegate<object>(_ => { });
        var middlewares = new IPipelineMiddleware<object>[]
        {
            PipelineMiddleware.FromDelegate<object>(async (ctx, next, ct) => await next(ctx, ct)),
            PipelineMiddleware.FromDelegate<object>(async (ctx, next, ct) => await next(ctx, ct)),
        };

        var pipeline = new Pipeline<object>(handler, middlewares);

        Assert.Equal(2, pipeline.MiddlewareCount);
    }

    [Fact]
    public void Constructor_MutatingTheSourceListAfterConstruction_DoesNotAffectPipeline()
    {
        // The pipeline snapshots the middlewares at construction time, so
        // mutating the caller's list afterwards must not change its behaviour.
        IPipelineHandler<object> handler = PipelineHandler.FromDelegate<object>(_ => { });
        var middlewares = new List<IPipelineMiddleware<object>>
        {
            PipelineMiddleware.FromDelegate<object>(async (ctx, next, ct) => await next(ctx, ct)),
        };

        var pipeline = new Pipeline<object>(handler, middlewares);

        middlewares.Add(PipelineMiddleware.FromDelegate<object>(async (ctx, next, ct) => await next(ctx, ct)));

        Assert.Equal(1, pipeline.MiddlewareCount);
    }

    private sealed class CountingHandler : IPipelineHandler<object>
    {
        public int CallCount { get; private set; }

        public Task HandleAsync(object context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
