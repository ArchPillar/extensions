namespace ArchPillar.Extensions.Pipelines;

public sealed class PipelineBuilderTests
{
    [Fact]
    public void Build_WithoutHandler_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => Pipeline.For<object>().Build());
        Assert.Contains("No handler configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithHandlerOnly_Succeeds()
    {
        Pipeline<object> pipeline = Pipeline.For<object>()
            .Handle(_ => { })
            .Build();

        Assert.Equal(0, pipeline.MiddlewareCount);
    }

    [Fact]
    public async Task Handle_SecondCall_ReplacesPreviousHandlerAsync()
    {
        var firstInvoked = false;
        var secondInvoked = false;

        Pipeline<object> pipeline = Pipeline.For<object>()
            .Handle(_ => firstInvoked = true)
            .Handle(_ => secondInvoked = true)
            .Build();

        await pipeline.ExecuteAsync(new object());

        Assert.False(firstInvoked);
        Assert.True(secondInvoked);
    }

    [Fact]
    public void Use_NullMiddlewareInstance_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Pipeline.For<object>().Use((IPipelineMiddleware<object>)null!));
    }

    [Fact]
    public void Handle_NullHandlerInstance_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Pipeline.For<object>().Handle((IPipelineHandler<object>)null!));
    }

    [Fact]
    public async Task Use_WithClassBasedMiddleware_IntegratesWithLambdaOverloadsAsync()
    {
        var log = new List<string>();

        Pipeline<List<string>> pipeline = Pipeline.For<List<string>>()
            .Use(new RecordingMiddleware("class"))
            .Use(async (ctx, next, ct) =>
            {
                ctx.Add("lambda:before");
                await next(ctx, ct);
                ctx.Add("lambda:after");
            })
            .Handle(ctx => ctx.Add("handler"))
            .Build();

        await pipeline.ExecuteAsync(log);

        Assert.Equal(
            new[]
            {
                "class:before",
                "lambda:before",
                "handler",
                "lambda:after",
                "class:after",
            },
            log);
    }

    [Fact]
    public async Task PipelineHandler_FromDelegate_AsyncOverload_WorksAsync()
    {
        var handled = false;
        IPipelineHandler<object> handler = PipelineHandler.FromDelegate<object>((_, _) =>
        {
            handled = true;
            return Task.CompletedTask;
        });

        var pipeline = new Pipeline<object>(handler, []);
        await pipeline.ExecuteAsync(new object());

        Assert.True(handled);
    }

    [Fact]
    public void PipelineHandler_FromDelegate_NullDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => PipelineHandler.FromDelegate<object>((Action<object>)null!));
        Assert.Throws<ArgumentNullException>(
            () => PipelineHandler.FromDelegate<object>((Func<object, Task>)null!));
        Assert.Throws<ArgumentNullException>(
            () => PipelineHandler.FromDelegate<object>((Func<object, CancellationToken, Task>)null!));
    }

    [Fact]
    public async Task PipelineMiddleware_FromDelegate_ShortOverload_ForwardsCancellationToNextAsync()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seenByHandler = default;

        Pipeline<object> pipeline = Pipeline
            .For<object>()
            .Use((ctx, next) => next(ctx))
            .Handle((_, ct) =>
            {
                seenByHandler = ct;
                return Task.CompletedTask;
            })
            .Build();

        await pipeline.ExecuteAsync(new object(), cts.Token);

        Assert.Equal(cts.Token, seenByHandler);
    }

    [Fact]
    public void PipelineMiddleware_FromDelegate_NullDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => PipelineMiddleware.FromDelegate<object>((Func<object, Func<object, Task>, Task>)null!));
        Assert.Throws<ArgumentNullException>(
            () => PipelineMiddleware.FromDelegate<object>((Func<object, PipelineDelegate<object>, CancellationToken, Task>)null!));
    }

    private sealed class RecordingMiddleware(string label) : IPipelineMiddleware<List<string>>
    {
        public async Task InvokeAsync(List<string> context, PipelineDelegate<List<string>> next, CancellationToken cancellationToken = default)
        {
            context.Add($"{label}:before");
            await next(context, cancellationToken);
            context.Add($"{label}:after");
        }
    }
}
