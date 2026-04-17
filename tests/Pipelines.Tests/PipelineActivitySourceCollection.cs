namespace ArchPillar.Extensions.Pipelines.Tests;

/// <summary>
/// xUnit collection used by tests that interact with the global
/// <see cref="PipelineActivitySource"/>. Members of this collection are run
/// serially, so tests that subscribe listeners (<c>ActivityMiddlewareTests</c>)
/// do not race with tests that require a no-listener environment
/// (<c>PipelineAllocationTests.ActivityMiddleware_WithNoListenerSubscribed_IsAllocationFree</c>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class PipelineActivitySourceCollection
{
    public const string Name = "PipelineActivitySource";
}
