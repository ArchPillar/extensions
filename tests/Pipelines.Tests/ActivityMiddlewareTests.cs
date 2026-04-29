using System.Diagnostics;

namespace ArchPillar.Extensions.Pipelines.Tests;

[Collection(PipelineActivitySourceCollection.Name)]
public sealed class ActivityMiddlewareTests
{
    // ------------------------------------------------------------------
    // Pass-through behaviour
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_WithNoListenerSubscribed_StillInvokesNextAsync()
    {
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext { OperationName = "X" };
        var ran = false;

        await middleware.InvokeAsync(
            context,
            (_, _) =>
            {
                ran = true;
                return Task.CompletedTask;
            });

        Assert.True(ran);
    }

    [Fact]
    public async Task InvokeAsync_PassesContextAndTokenThroughToNextAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext { OperationName = "X" };
        using var cts = new CancellationTokenSource();

        TestContext? seenContext = null;
        CancellationToken seenToken = default;

        await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                seenContext = ctx;
                seenToken = ct;
                return Task.CompletedTask;
            },
            cts.Token);

        Assert.Same(context, seenContext);
        Assert.Equal(cts.Token, seenToken);
    }

    // ------------------------------------------------------------------
    // Activity creation
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_WithListener_StartsActivityWithOperationNameAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext { OperationName = "Orders.Place" };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask);

        Activity activity = Assert.Single(fixture.Activities);
        Assert.Equal("Orders.Place", activity.DisplayName);
    }

    [Fact]
    public async Task InvokeAsync_ActivityComesFromLibraryActivitySourceAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();

        await middleware.InvokeAsync(new TestContext { OperationName = "X" }, (_, _) => Task.CompletedTask);

        Assert.Equal(PipelineActivitySource.Name, fixture.Activities[0].Source.Name);
    }

    [Fact]
    public async Task InvokeAsync_UsesActivityKindFromContextAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext
        {
            OperationName = "X",
            Kind = ActivityKind.Consumer,
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask);

        Assert.Equal(ActivityKind.Consumer, fixture.Activities[0].Kind);
    }

    [Fact]
    public async Task InvokeAsync_DefaultActivityKindIsInternalAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();

        await middleware.InvokeAsync(new TestContext { OperationName = "X" }, (_, _) => Task.CompletedTask);

        Assert.Equal(ActivityKind.Internal, fixture.Activities[0].Kind);
    }

    [Fact]
    public async Task InvokeAsync_UsesParentContextFromContextForRemoteParentAsync()
    {
        using var fixture = new ListenerFixture();
        var parent = new ActivityContext(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);

        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext
        {
            OperationName = "X",
            Parent = parent,
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask);

        Activity activity = fixture.Activities[0];
        Assert.Equal(parent.TraceId, activity.TraceId);
        Assert.Equal(parent.SpanId, activity.ParentSpanId);
    }

    [Fact]
    public async Task InvokeAsync_DefaultParentContextFallsBackToActivityCurrentAsync()
    {
        using var fixture = new ListenerFixture();
        using Activity? outer = PipelineActivitySource.Instance.StartActivity("outer");
        Assert.NotNull(outer);

        var middleware = new ActivityMiddleware<TestContext>();

        await middleware.InvokeAsync(new TestContext { OperationName = "inner" }, (_, _) => Task.CompletedTask);

        Activity inner = fixture.Activities.Single(a => a.DisplayName == "inner");
        Assert.Equal(outer.TraceId, inner.TraceId);
        Assert.Equal(outer.SpanId, inner.ParentSpanId);
    }

    // ------------------------------------------------------------------
    // Enrichment
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_CallsEnrichActivityWithStartedActivityAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext
        {
            OperationName = "X",
            Enrich = a => a.SetTag("order.id", 42),
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask);

        Assert.Equal(42, fixture.Activities[0].GetTagItem("order.id"));
    }

    [Fact]
    public async Task InvokeAsync_EnrichActivityIsCalledBeforeNextAsync()
    {
        using var fixture = new ListenerFixture();
        var log = new List<string>();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext
        {
            OperationName = "X",
            Enrich = _ => log.Add("enrich"),
        };

        await middleware.InvokeAsync(
            context,
            (_, _) =>
            {
                log.Add("next");
                return Task.CompletedTask;
            });

        Assert.Equal(new[] { "enrich", "next" }, log);
    }

    [Fact]
    public async Task InvokeAsync_WithNoListener_EnrichIsNotCalledAsync()
    {
        var enrichCalled = false;
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext
        {
            OperationName = "X",
            Enrich = _ => enrichCalled = true,
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask);

        Assert.False(enrichCalled);
    }

    // ------------------------------------------------------------------
    // Exceptions
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_RecordsExceptionEventAndSetsErrorStatusAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext { OperationName = "X" };
        var thrown = new InvalidOperationException("boom");

        InvalidOperationException captured = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, (_, _) => throw thrown));

        Assert.Same(thrown, captured);

        Activity activity = fixture.Activities[0];
        Assert.Equal(ActivityStatusCode.Error, activity.Status);

        ActivityEvent evt = Assert.Single(activity.Events);
        Assert.Equal("exception", evt.Name);

        var tags = evt.Tags.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["exception.type"]);
        Assert.Equal("boom", tags["exception.message"]);
        Assert.NotNull(tags["exception.stacktrace"]);
        Assert.True((bool?)tags["exception.escaped"]);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_ExceptionIsRethrownAsync()
    {
        using var fixture = new ListenerFixture();
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext { OperationName = "X" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(
                context,
                (_, _) => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task InvokeAsync_WithNoListener_WhenNextThrows_ExceptionStillPropagatesAsync()
    {
        var middleware = new ActivityMiddleware<TestContext>();
        var context = new TestContext { OperationName = "X" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(
                context,
                (_, _) => throw new InvalidOperationException("boom")));
    }

    // ------------------------------------------------------------------
    // Null-argument validation
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsAsync()
    {
        var middleware = new ActivityMiddleware<TestContext>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => middleware.InvokeAsync(null!, (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task InvokeAsync_NullNext_ThrowsAsync()
    {
        var middleware = new ActivityMiddleware<TestContext>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => middleware.InvokeAsync(new TestContext { OperationName = "X" }, null!));
    }

    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    private sealed class TestContext : IPipelineContext
    {
        public string OperationName { get; init; } = "";
        public ActivityKind Kind { get; init; } = ActivityKind.Internal;
        public ActivityContext Parent { get; init; }
        public Action<Activity>? Enrich { get; init; }

        ActivityKind IPipelineContext.ActivityKind => Kind;
        ActivityContext IPipelineContext.ParentContext => Parent;
        void IPipelineContext.EnrichActivity(Activity activity) => Enrich?.Invoke(activity);
    }

    private sealed class ListenerFixture : IDisposable
    {
        private readonly ActivityListener _listener;

        public List<Activity> Activities { get; } = [];

        public ListenerFixture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == PipelineActivitySource.Name,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = Activities.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }
}
