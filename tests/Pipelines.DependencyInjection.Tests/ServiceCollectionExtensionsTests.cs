using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Pipelines.DependencyInjection.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    // -----------------------------------------------------------------------
    // AddPipeline<T, THandler> — pipeline + handler composition
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddPipeline_WithHandlerAndMiddlewares_ResolvesAndRunsInRegistrationOrderAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext, RecordingHandler>();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();
        services.AddPipelineMiddleware<TestContext, SecondMiddleware>();
        services.AddPipelineMiddleware<TestContext, ThirdMiddleware>();

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
    public async Task AddPipeline_WithHandlerOnly_ResolvesAndRunsAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext, RecordingHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(new[] { "handler" }, journal.Events);
        Assert.Equal(0, pipeline.MiddlewareCount);
    }

    [Fact]
    public async Task AddPipeline_MiddlewareWithConstructorDependency_ResolvesDependencyFromContainerAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddSingleton(new DependencyWithValue("injected"));
        services.AddPipeline<TestContext, RecordingHandler>();
        services.AddPipelineMiddleware<TestContext, DependencyMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(new[] { "dep:injected", "handler" }, journal.Events);
    }

    // -----------------------------------------------------------------------
    // AddPipeline<T> delegate overloads
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddPipeline_AsyncDelegateHandlerWithToken_ResolvesAndRunsAsync()
    {
        var handled = false;
        CancellationToken seenByHandler = default;

        var services = new ServiceCollection();
        services.AddPipeline<TestContext>((ctx, ct) =>
        {
            handled = true;
            seenByHandler = ct;
            ctx.Handled = true;
            return Task.CompletedTask;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using var cts = new CancellationTokenSource();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        await pipeline.ExecuteAsync(new TestContext(), cts.Token);

        Assert.True(handled);
        Assert.Equal(cts.Token, seenByHandler);
    }

    [Fact]
    public async Task AddPipeline_AsyncDelegateHandler_ResolvesAndRunsAsync()
    {
        var handled = false;
        var services = new ServiceCollection();
        services.AddPipeline<TestContext>(_ =>
        {
            handled = true;
            return Task.CompletedTask;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        await pipeline.ExecuteAsync(new TestContext());

        Assert.True(handled);
    }

    [Fact]
    public async Task AddPipeline_SyncDelegateHandler_ResolvesAndRunsAsync()
    {
        var handled = false;
        var services = new ServiceCollection();
        services.AddPipeline<TestContext>((Action<TestContext>)(_ => handled = true));

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        await pipeline.ExecuteAsync(new TestContext());

        Assert.True(handled);
    }

    [Fact]
    public async Task AddPipeline_DelegateHandler_ComposesWithRegisteredMiddlewaresAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext>(ctx =>
        {
            ctx.Journal!.Events.Add("delegate-handler");
            return Task.CompletedTask;
        });
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();
        var context = new TestContext { Journal = journal };

        await pipeline.ExecuteAsync(context);

        Assert.Equal(
            new[] { "first:before", "delegate-handler", "first:after" },
            journal.Events);
    }

    // -----------------------------------------------------------------------
    // AddPipelineMiddleware<T, TMiddleware> — standalone, ignores pipeline
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipelineMiddleware_WithoutPipelineRegistered_DoesNotRegisterPipeline()
    {
        var services = new ServiceCollection();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();

        Assert.DoesNotContain(services, s => s.ServiceType == typeof(Pipeline<TestContext>));
        Assert.Single(services, s => s.ServiceType == typeof(IPipelineMiddleware<TestContext>));
    }

    [Fact]
    public async Task AddPipelineMiddleware_FromLibraryModule_ContributesToExternallyOwnedPipelineAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();

        // "Library module" contributes a middleware without knowing about the pipeline.
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();

        // "Application" owns the pipeline registration.
        services.AddPipeline<TestContext, RecordingHandler>();
        services.AddPipelineMiddleware<TestContext, SecondMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[] { "first:before", "second:before", "handler", "second:after", "first:after" },
            journal.Events);
    }

    [Fact]
    public void AddPipelineMiddleware_SameMiddlewareTwice_IsDeduplicated()
    {
        var services = new ServiceCollection();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();

        ServiceDescriptor[] middlewareDescriptors = services
            .Where(s => s.ServiceType == typeof(IPipelineMiddleware<TestContext>))
            .ToArray();

        Assert.Single(middlewareDescriptors);
    }

    // -----------------------------------------------------------------------
    // ReplacePipelineHandler — swap out the existing handler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReplacePipelineHandler_WithClass_RemovesOriginalAndUsesReplacementAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext, RecordingHandler>();
        services.ReplacePipelineHandler<TestContext, AnotherHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(new[] { "another-handler" }, journal.Events);
        Assert.Single(services, s => s.ServiceType == typeof(IPipelineHandler<TestContext>));
    }

    [Fact]
    public async Task ReplacePipelineHandler_WithAsyncDelegate_RunsReplacementAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddPipeline<TestContext, RecordingHandler>();

        services.ReplacePipelineHandler<TestContext>((ctx, _) =>
        {
            ctx.Handled = true;
            return Task.CompletedTask;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        var context = new TestContext();
        await pipeline.ExecuteAsync(context);

        Assert.True(context.Handled);
    }

    [Fact]
    public async Task ReplacePipelineHandler_WithSyncDelegate_RunsReplacementAsync()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>();
        services.ReplacePipelineHandler<TestContext>((Action<TestContext>)(ctx => ctx.Handled = true));

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        var context = new TestContext();
        await pipeline.ExecuteAsync(context);

        Assert.True(context.Handled);
    }

    [Fact]
    public async Task ReplacePipelineHandler_WhenNoHandlerRegistered_AddsOneAsync()
    {
        var services = new ServiceCollection();
        services.ReplacePipelineHandler<TestContext>((Action<TestContext>)(ctx => ctx.Handled = true));

        Assert.Single(services, s => s.ServiceType == typeof(IPipelineHandler<TestContext>));

        // Still runs if a pipeline is registered afterward.
        services.AddPipeline<TestContext, RecordingHandler>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        var context = new TestContext();
        await pipeline.ExecuteAsync(context);

        Assert.True(context.Handled);
    }

    // -----------------------------------------------------------------------
    // Lifetime validation
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_SingletonPipeline_AllowsSingletonHandler()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>(
            pipelineLifetime: ServiceLifetime.Singleton,
            handlerLifetime: ServiceLifetime.Singleton);

        ServiceDescriptor pipelineDescriptor = services.Single(s => s.ServiceType == typeof(Pipeline<TestContext>));
        ServiceDescriptor handlerDescriptor = services.Single(s => s.ServiceType == typeof(IPipelineHandler<TestContext>));

        Assert.Equal(ServiceLifetime.Singleton, pipelineDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, handlerDescriptor.Lifetime);
    }

    [Fact]
    public void AddPipeline_SingletonPipeline_WithExplicitScopedHandler_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPipeline<TestContext, RecordingHandler>(
                pipelineLifetime: ServiceLifetime.Singleton,
                handlerLifetime: ServiceLifetime.Scoped));

        Assert.Contains("singleton", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPipelineMiddleware_Scoped_WhenPipelineIsSingleton_Throws()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>(
            pipelineLifetime: ServiceLifetime.Singleton,
            handlerLifetime: ServiceLifetime.Singleton);

        Assert.Throws<InvalidOperationException>(
            () => services.AddPipelineMiddleware<TestContext, FirstMiddleware>(ServiceLifetime.Scoped));
    }

    [Fact]
    public void AddPipeline_SingletonPipeline_WhenScopedMiddlewareAlreadyRegistered_Throws()
    {
        var services = new ServiceCollection();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>(ServiceLifetime.Scoped);

        Assert.Throws<InvalidOperationException>(
            () => services.AddPipeline<TestContext, RecordingHandler>(
                pipelineLifetime: ServiceLifetime.Singleton,
                handlerLifetime: ServiceLifetime.Singleton));
    }

    [Fact]
    public void ReplacePipelineHandler_Scoped_WhenPipelineIsSingleton_Throws()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>(
            pipelineLifetime: ServiceLifetime.Singleton,
            handlerLifetime: ServiceLifetime.Singleton);

        Assert.Throws<InvalidOperationException>(
            () => services.ReplacePipelineHandler<TestContext, AnotherHandler>(ServiceLifetime.Scoped));
    }

    [Fact]
    public void AddPipeline_ScopedPipeline_AllowsSingletonMiddleware()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>(
            pipelineLifetime: ServiceLifetime.Scoped,
            handlerLifetime: ServiceLifetime.Scoped);
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>(ServiceLifetime.Singleton);

        // No throw — scoped pipeline can depend on singleton middleware.
        ServiceDescriptor middlewareDescriptor = services.Single(
            s => s.ServiceType == typeof(IPipelineMiddleware<TestContext>));
        Assert.Equal(ServiceLifetime.Singleton, middlewareDescriptor.Lifetime);
    }

    [Fact]
    public void AddPipeline_HandlerLifetimeDefaultsToPipelineLifetime()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>(pipelineLifetime: ServiceLifetime.Transient);

        ServiceDescriptor pipelineDescriptor = services.Single(s => s.ServiceType == typeof(Pipeline<TestContext>));
        ServiceDescriptor handlerDescriptor = services.Single(s => s.ServiceType == typeof(IPipelineHandler<TestContext>));

        Assert.Equal(ServiceLifetime.Transient, pipelineDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, handlerDescriptor.Lifetime);
    }

    // -----------------------------------------------------------------------
    // Idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_CalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddPipeline<TestContext, RecordingHandler>();
        services.AddPipeline<TestContext, AnotherHandler>();

        ServiceDescriptor[] pipelineDescriptors = services
            .Where(s => s.ServiceType == typeof(Pipeline<TestContext>))
            .ToArray();
        ServiceDescriptor[] handlerDescriptors = services
            .Where(s => s.ServiceType == typeof(IPipelineHandler<TestContext>))
            .ToArray();

        Assert.Single(pipelineDescriptors);
        Assert.Single(handlerDescriptors);
        Assert.Equal(typeof(RecordingHandler), handlerDescriptors[0].ImplementationType);
    }

    // -----------------------------------------------------------------------
    // External IPipelineMiddleware<T> registrations are respected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExternalIPipelineMiddlewareRegistration_IsIncludedInPipelineAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestJournal>();
        services.AddScoped<IPipelineMiddleware<TestContext>, UnrelatedMiddleware>();

        services.AddPipeline<TestContext, RecordingHandler>();
        services.AddPipelineMiddleware<TestContext, FirstMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Pipeline<TestContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<TestContext>>();
        TestJournal journal = scope.ServiceProvider.GetRequiredService<TestJournal>();

        await pipeline.ExecuteAsync(new TestContext());

        Assert.Equal(
            new[] { "unrelated", "first:before", "handler", "first:after" },
            journal.Events);
    }

    // -----------------------------------------------------------------------
    // Null-argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPipeline_NullServiceCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddPipeline<TestContext, RecordingHandler>(null!));
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddPipeline<TestContext>(null!, (Action<TestContext>)(_ => { })));
    }

    [Fact]
    public void AddPipeline_NullDelegate_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(
            () => services.AddPipeline<TestContext>((Func<TestContext, CancellationToken, Task>)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.AddPipeline<TestContext>((Func<TestContext, Task>)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.AddPipeline<TestContext>((Action<TestContext>)null!));
    }

    [Fact]
    public void AddPipelineMiddleware_NullServiceCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddPipelineMiddleware<TestContext, FirstMiddleware>(null!));
    }

    [Fact]
    public void ReplacePipelineHandler_NullServiceCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.ReplacePipelineHandler<TestContext, RecordingHandler>(null!));
    }

    [Fact]
    public void ReplacePipelineHandler_NullDelegate_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(
            () => services.ReplacePipelineHandler<TestContext>((Func<TestContext, CancellationToken, Task>)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.ReplacePipelineHandler<TestContext>((Func<TestContext, Task>)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.ReplacePipelineHandler<TestContext>((Action<TestContext>)null!));
    }

    // =======================================================================
    // Test fixtures
    // =======================================================================

    public sealed class TestContext
    {
        public bool Handled { get; set; }
        public TestJournal? Journal { get; set; }
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
