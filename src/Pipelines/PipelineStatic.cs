namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Static entry point for building a <see cref="Pipeline{T}"/> without a DI
/// container. Use this when you want to wire up a pipeline manually in a
/// test, a console program, or any component that instantiates its own
/// collaborators.
/// </summary>
/// <remarks>
/// <code>
/// var pipeline = Pipeline
///     .For&lt;MyContext&gt;()
///     .Use(new LoggingMiddleware())
///     .Use(async (ctx, next, ct) =&gt;
///     {
///         if (ctx.ShouldSkip) return;
///         await next(ctx, ct);
///     })
///     .Handle(new MyHandler())
///     .Build();
///
/// await pipeline.ExecuteAsync(context);
/// </code>
/// <para>
/// For DI-hosted applications, prefer the <c>services.AddPipeline&lt;T, THandler&gt;()</c>
/// extension on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </para>
/// </remarks>
public static class Pipeline
{
    /// <summary>
    /// Starts building a new <see cref="Pipeline{T}"/> for the given context type.
    /// </summary>
    /// <typeparam name="T">The context type the pipeline will process.</typeparam>
    /// <returns>A fluent <see cref="PipelineBuilder{T}"/>.</returns>
    public static PipelineBuilder<T> For<T>() => new();
}
