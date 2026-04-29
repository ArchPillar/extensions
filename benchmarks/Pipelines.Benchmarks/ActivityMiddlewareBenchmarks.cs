using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace ArchPillar.Extensions.Pipelines.Benchmarks;

/// <summary>
/// Benchmarks for <see cref="ActivityMiddleware{T}"/> that isolate the cost
/// of the middleware itself in two scenarios:
/// <list type="bullet">
/// <item>
/// <description>
/// <b>No listener subscribed</b> — the expected shape of the real world.
/// The middleware detects the null activity and tail-calls
/// <c>next(...)</c>. Should allocate zero bytes and cost only a few
/// nanoseconds over the plain-pipeline baseline.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Listener subscribed (sampling all data)</b> — exercises the full
/// tracing path: activity creation, enrichment, dispose. Documents the
/// cost of enabling telemetry.
/// </description>
/// </item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public sealed class ActivityMiddlewareBenchmarks
{
    private TracedContext _context = null!;
    private Pipeline<TracedContext> _baseline = null!;
    private Pipeline<TracedContext> _withActivityMiddleware = null!;
    private ActivityListener? _listener;

    [Params(false, true)]
    public bool ListenerSubscribed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _context = new TracedContext();

        _baseline = new Pipeline<TracedContext>(new NoopHandler(), []);
        _withActivityMiddleware = new Pipeline<TracedContext>(
            new NoopHandler(),
            [new ActivityMiddleware<TracedContext>()]);

        if (ListenerSubscribed)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == PipelineActivitySource.Name,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(_listener);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _listener?.Dispose();

    [Benchmark(Baseline = true, Description = "Pipeline: 0 middlewares")]
    public Task Baseline_NoMiddlewareAsync() => _baseline.ExecuteAsync(_context);

    [Benchmark(Description = "Pipeline: ActivityMiddleware")]
    public Task ActivityMiddlewareAsync() => _withActivityMiddleware.ExecuteAsync(_context);

    public sealed class TracedContext : IPipelineContext
    {
        public string OperationName => "Benchmarks.Activity";

        public void EnrichActivity(Activity activity)
            => activity.SetTag("benchmark.context", "traced");
    }

    public sealed class NoopHandler : IPipelineHandler<TracedContext>
    {
        public Task HandleAsync(TracedContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
