using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Pipelines.DependencyInjection.Tests;

public class ServiceCollectionExtensionsTests
{
    // -----------------------------------------------------------------------
    // Pipeline resolves and runs — standard DI composition
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
    // Forgiving registration (TryAdd / TryAddEnumerable semantics)
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_CalledTwiceForSameContextType_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext>().Handle<RecordingHandler>();
        services.AddPipeline<TestContext>().Handle<AnotherHandler>();

        ServiceDescriptor[] pipelineDescriptors = services
            .Where(s => s.ServiceType == typeof(Pipeline<TestContext>))
            .ToArray();
        ServiceDescriptor[] handlerDescriptors = services
            .Where(s => s.ServiceType == typeof(IPipelineHandler<TestContext>))
            .ToArray();

        // Only one Pipeline<T> descriptor — the second AddPipeline was a no-op.
        Assert.Single(pipelineDescriptors);

        // Only one handler descriptor — the first call wins.
        Assert.Single(handlerDescriptors);
        Assert.Equal(typeof(RecordingHandler), handlerDescriptors[0].ImplementationType);
    }

    [Fact]
    public void Use_SameMiddlewareRegisteredTwice_IsDeduplicated()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Use<FirstMiddleware>()
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        ServiceDescriptor[] middlewareDescriptors = services
            .Where(s => s.ServiceType == typeof(IPipelineMiddleware<TestContext>))
            .ToArray();

        Assert.Single(middlewareDescriptors);
        Assert.Equal(typeof(FirstMiddleware), middlewareDescriptors[0].ImplementationType);
    }

    [Fact]
    public async Task Handle_CalledTwice_FirstRegistrationWinsAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>()
            .Handle<RecordingHandler>()
            .Handle<AnotherHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(new[] { "handler" }, journal.Events);
    }

    // -----------------------------------------------------------------------
    // Proper DI composition — external registrations are respected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExternalIPipelineMiddlewareRegistration_IsIncludedInPipelineAsync()
    {
        // Register an IPipelineMiddleware<T> directly, without going through
        // AddPipeline<T>. Because AddPipeline<T> composes via the container,
        // this middleware IS picked up by the pipeline.
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddScoped<IPipelineMiddleware<TestContext>, UnrelatedMiddleware>();

        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        // The externally-registered middleware runs first (registered first),
        // followed by the Use<FirstMiddleware>() registration, then the handler.
        Assert.Equal(
            new[] { "unrelated", "first:before", "handler", "first:after" },
            journal.Events);
    }

    [Fact]
    public async Task MultipleModulesEachAddingMiddleware_AllContributionsAreComposedAsync()
    {
        // Two independent "modules" each register middlewares for the same
        // pipeline. TryAddEnumerable stacks them without conflict.
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();

        // Module A
        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>()
            .Handle<RecordingHandler>();

        // Module B — also configures the same pipeline type
        services.AddPipeline<TestContext>()
            .Use<SecondMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[] { "first:before", "second:before", "handler", "second:after", "first:after" },
            journal.Events);
    }

    // -----------------------------------------------------------------------
    // Missing handler — DI surfaces a resolution error
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_WithoutHandler_ThrowsFromDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>()
            .Use<FirstMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        // DI can't satisfy Pipeline<TestContext>'s IPipelineHandler<TestContext>
        // constructor parameter because no handler was registered.
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
        ServiceDescriptor middlewareDescriptor = services.Single(s => s.ServiceType == typeof(IPipelineMiddleware<TestContext>));
        ServiceDescriptor handlerDescriptor = services.Single(s => s.ServiceType == typeof(IPipelineHandler<TestContext>));

        Assert.Equal(ServiceLifetime.Singleton, pipelineDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, middlewareDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, handlerDescriptor.Lifetime);
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

    public sealed class AnotherHandler(TestJournal journal) : IPipelineHandler<TestContext>
    {
        public Task HandleAsync(TestContext context, CancellationToken cancellationToken = default)
        {
            journal.Events.Add("another-handler");
            return Task.CompletedTask;
        }
    }
}
