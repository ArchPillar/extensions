using BenchmarkDotNet.Attributes;

namespace ArchPillar.Extensions.Pipelines.Benchmarks;

/// <summary>
/// Benchmarks for <see cref="Pipeline{T}"/> on the synchronous, allocation-free
/// hot path. All middlewares are trivial pass-throughs that tail-call
/// <c>next</c>, and the handler returns a cached <see cref="Task.CompletedTask"/> —
/// this is the scenario where the pipeline's own overhead can be measured
/// without any middleware/handler cost leaking into the numbers.
/// </summary>
/// <remarks>
/// <para>
/// Run with <c>dotnet run -c Release --project benchmarks/Pipelines.Benchmarks</c>.
/// </para>
/// <para>
/// The expected shape of the results:
/// </para>
/// <list type="bullet">
/// <item>Every row allocates zero bytes (Allocated column = 0 B or "-").</item>
/// <item>The pipeline's overhead per middleware layer is in single-digit nanoseconds.</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class PipelineBenchmarks
{
    private BenchmarkContext _context = null!;
    private IPipelineHandler<BenchmarkContext> _handler = null!;
    private Pipeline<BenchmarkContext> _handlerOnly = null!;
    private Pipeline<BenchmarkContext> _oneMiddleware = null!;
    private Pipeline<BenchmarkContext> _threeMiddlewares = null!;
    private Pipeline<BenchmarkContext> _tenMiddlewares = null!;

    [GlobalSetup]
    public void Setup()
    {
        _context = new BenchmarkContext();
        _handler = new NoopHandler();

        _handlerOnly = new Pipeline<BenchmarkContext>(_handler, []);

        _oneMiddleware = new Pipeline<BenchmarkContext>(
            _handler,
            [new PassthroughMiddleware()]);

        _threeMiddlewares = new Pipeline<BenchmarkContext>(
            _handler,
            [
                new PassthroughMiddleware(),
                new PassthroughMiddleware(),
                new PassthroughMiddleware(),
            ]);

        var tenLayers = new IPipelineMiddleware<BenchmarkContext>[10];
        for (var i = 0; i < tenLayers.Length; i++)
        {
            tenLayers[i] = new PassthroughMiddleware();
        }

        _tenMiddlewares = new Pipeline<BenchmarkContext>(_handler, tenLayers);
    }

    [Benchmark(Baseline = true, Description = "Direct handler call (no pipeline)")]
    public Task DirectHandlerCallAsync() => _handler.HandleAsync(_context);

    [Benchmark(Description = "Pipeline: 0 middlewares")]
    public Task Pipeline_HandlerOnlyAsync() => _handlerOnly.ExecuteAsync(_context);

    [Benchmark(Description = "Pipeline: 1 middleware")]
    public Task Pipeline_OneMiddlewareAsync() => _oneMiddleware.ExecuteAsync(_context);

    [Benchmark(Description = "Pipeline: 3 middlewares")]
    public Task Pipeline_ThreeMiddlewaresAsync() => _threeMiddlewares.ExecuteAsync(_context);

    [Benchmark(Description = "Pipeline: 10 middlewares")]
    public Task Pipeline_TenMiddlewaresAsync() => _tenMiddlewares.ExecuteAsync(_context);

    public sealed class BenchmarkContext;

    /// <summary>
    /// Handler that returns the cached <see cref="Task.CompletedTask"/> —
    /// allocation-free synchronous completion.
    /// </summary>
    public sealed class NoopHandler : IPipelineHandler<BenchmarkContext>
    {
        public Task HandleAsync(BenchmarkContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Middleware that tail-calls <c>next</c> without <c>async</c>/<c>await</c>,
    /// so no state machine is allocated on the synchronous path.
    /// </summary>
    public sealed class PassthroughMiddleware : IPipelineMiddleware<BenchmarkContext>
    {
        public Task InvokeAsync(BenchmarkContext context, PipelineDelegate<BenchmarkContext> next, CancellationToken cancellationToken = default)
            => next(context, cancellationToken);
    }
}
