using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Primitives.DependencyInjection.Tests;

public class ServiceCollectionExtensionsTests
{
    // -----------------------------------------------------------------------
    // Pipeline resolves and runs
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddPipeline_WithMiddlewaresAndHandler_ResolvesAndRunsInRegistrationOrderAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Use<SecondMiddleware>()
            .Use<ThirdMiddleware>()
            .Handle<RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[]
            {
                "first:before",
                "second:before",
                "third:before",
                "handler",
                "third:after",
                "second:after",
                "first:after",
            },
            journal.Events);
    }

    [Fact]
    public async Task AddPipeline_WithOnlyHandler_ResolvesAndRunsAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>()
            .Handle<RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(new[] { "handler" }, journal.Events);
        Assert.Equal(0, pipeline.MiddlewareCount);
    }

    // -----------------------------------------------------------------------
    // Constructor injection into middlewares/handlers
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddPipeline_MiddlewareWithConstructorDependency_ResolvesDependencyFromContainerAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddSingleton(new DependencyWithValue("injected"));
        services.AddPipeline<TestContext>()
            .Use<DependencyMiddleware>()
            .Handle<RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[] { "dep:injected", "handler" },
            journal.Events);
    }

    // -----------------------------------------------------------------------
    // Isolation from the global IPipelineMiddleware<T> service namespace
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddPipeline_DoesNotRegisterMiddlewaresAsGlobalIPipelineMiddlewareAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IEnumerable<IPipelineMiddleware<TestContext>> globalMiddlewares =
            scope.ServiceProvider.GetServices<IPipelineMiddleware<TestContext>>();

        Assert.Empty(globalMiddlewares);

        // And the pipeline still runs the middleware it knows about.
        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();
        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[] { "first:before", "handler", "first:after" },
            journal.Events);
    }

    [Fact]
    public async Task AddPipeline_UnrelatedIPipelineMiddlewareRegistrationsAreNotAttachedToThePipelineAsync()
    {
        // An unrelated middleware registered directly as IPipelineMiddleware<T>
        // must not leak into the pipeline built via AddPipeline<T>().
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddSingleton<IPipelineMiddleware<TestContext>, UnrelatedMiddleware>();

        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        // Only the explicitly-added first middleware runs, not UnrelatedMiddleware.
        Assert.DoesNotContain("unrelated", journal.Events);
        Assert.Equal(
            new[] { "first:before", "handler", "first:after" },
            journal.Events);
    }

    // -----------------------------------------------------------------------
    // Duplicate and misuse detection
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_CalledTwiceForSameContextType_Throws()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext>();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPipeline<TestContext>());
        Assert.Contains("already registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handle_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        PipelineServiceBuilder<TestContext> builder = services.AddPipeline<TestContext>()
            .Handle<RecordingHandler>();

        Assert.Throws<InvalidOperationException>(() => builder.Handle<AnotherHandler>());
    }

    [Fact]
    public void Resolve_WithoutHandler_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>());
    }

    // -----------------------------------------------------------------------
    // Lifetime knob
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_DefaultsToScopedLifetime()
    {
        var services = new ServiceCollection();
        PipelineServiceBuilder<TestContext> builder = services.AddPipeline<TestContext>();

        Assert.Equal(ServiceLifetime.Scoped, builder.Lifetime);
    }

    [Fact]
    public void AddPipeline_SingletonLifetime_AppliesToPipelineAndRegisteredCollaborators()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext>(ServiceLifetime.Singleton)
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        ServiceDescriptor pipelineDescriptor = services.Single(s => s.ServiceType == typeof(Pipeline<TestContext>));
        ServiceDescriptor middlewareDescriptor = services.Single(s => s.ServiceType == typeof(FirstMiddleware));
        ServiceDescriptor handlerDescriptor = services.Single(s => s.ServiceType == typeof(RecordingHandler));

        Assert.Equal(ServiceLifetime.Singleton, pipelineDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, middlewareDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, handlerDescriptor.Lifetime);
    }

    // -----------------------------------------------------------------------
    // Pre-registered concrete types are reused (TryAdd semantics)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddPipeline_PreRegisteredMiddlewareConcreteType_IsReusedInsteadOfDuplicatedAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();

        // Explicit concrete-type registration
        services.AddSingleton<FirstMiddleware>();

        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        ServiceDescriptor middlewareDescriptor = services.Single(s => s.ServiceType == typeof(FirstMiddleware));

        // The existing Singleton registration wins - AddPipeline uses TryAdd and does not downgrade to Scoped.
        Assert.Equal(ServiceLifetime.Singleton, middlewareDescriptor.Lifetime);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();
        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[] { "first:before", "handler", "first:after" },
            journal.Events);
    }

    // -----------------------------------------------------------------------
    // Null-argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_NullServiceCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddPipeline<TestContext>(null!));
    }

    // =======================================================================
    // Test fixtures
    // =======================================================================

    public sealed class TestContext
    {
        public bool Handled { get; set; }
    }

    public sealed class TestJournal
    {
        public List<string> Events { get; } = [];
    }

    public sealed class DependencyWithValue(string value)
    {
        public string Value { get; } = value;
    }

    public sealed class FirstMiddleware(TestJournal journal) : IPipelineMiddleware<TestContext>
    {
        public async Task InvokeAsync(TestContext context, PipelineDelegate<TestContext> next, CancellationToken cancellationToken = default)
        {
            journal.Events.Add("first:before");
            await next(context, cancellationToken);
            journal.Events.Add("first:after");
        }
    }

    public sealed class SecondMiddleware(TestJournal journal) : IPipelineMiddleware<TestContext>
    {
        public async Task InvokeAsync(TestContext context, PipelineDelegate<TestContext> next, CancellationToken cancellationToken = default)
        {
            journal.Events.Add("second:before");
            await next(context, cancellationToken);
            journal.Events.Add("second:after");
        }
    }

    public sealed class ThirdMiddleware(TestJournal journal) : IPipelineMiddleware<TestContext>
    {
        public async Task InvokeAsync(TestContext context, PipelineDelegate<TestContext> next, CancellationToken cancellationToken = default)
        {
            journal.Events.Add("third:before");
            await next(context, cancellationToken);
            journal.Events.Add("third:after");
        }
    }

    public sealed class DependencyMiddleware(TestJournal journal, DependencyWithValue dependency) : IPipelineMiddleware<TestContext>
    {
        public async Task InvokeAsync(TestContext context, PipelineDelegate<TestContext> next, CancellationToken cancellationToken = default)
        {
            journal.Events.Add($"dep:{dependency.Value}");
            await next(context, cancellationToken);
        }
    }

    public sealed class UnrelatedMiddleware(TestJournal journal) : IPipelineMiddleware<TestContext>
    {
        public async Task InvokeAsync(TestContext context, PipelineDelegate<TestContext> next, CancellationToken cancellationToken = default)
        {
            journal.Events.Add("unrelated");
            await next(context, cancellationToken);
        }
    }

    public sealed class RecordingHandler(TestJournal journal) : IPipelineHandler<TestContext>
    {
        public Task HandleAsync(TestContext context, CancellationToken cancellationToken = default)
        {
            journal.Events.Add("handler");
            return Task.CompletedTask;
        }
    }

    public sealed class AnotherHandler : IPipelineHandler<TestContext>
    {
        public Task HandleAsync(TestContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
