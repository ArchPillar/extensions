using ArchPillar.Extensions.Primitives;

// ---------------------------------------------------------------------------
// Pipeline.BuilderSample
//
// Demonstrates building a Pipeline<T> by hand — no DI container involved.
// Uses the static Pipeline.For<T>() entry point and the fluent
// PipelineBuilder<T>, combining lambda-based middlewares, a class-based
// middleware, and a class-based handler.
// ---------------------------------------------------------------------------

var pipeline = Pipeline
    .For<OrderContext>()
    .Use(new LoggingMiddleware("outer"))
    .Use(async (ctx, next, ct) =>
    {
        // Inline lambda middleware — guard / short-circuit
        if (ctx.OrderId <= 0)
        {
            Console.WriteLine($"skip: invalid order {ctx.OrderId}");
            return;
        }

        await next(ctx, ct);
    })
    .Use(new LoggingMiddleware("inner"))
    .Handle(new PlaceOrderHandler())
    .Build();

Console.WriteLine("== happy path ==");
await pipeline.ExecuteAsync(new OrderContext { OrderId = 42 });

Console.WriteLine();
Console.WriteLine("== short-circuit path ==");
await pipeline.ExecuteAsync(new OrderContext { OrderId = 0 });

// ---------------------------------------------------------------------------
// Context + handler + middleware classes
// ---------------------------------------------------------------------------

internal sealed class OrderContext
{
    public int OrderId { get; set; }
}

internal sealed class LoggingMiddleware(string label) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{label}] order={context.OrderId} before");
        await next(context, cancellationToken);
        Console.WriteLine($"[{label}] order={context.OrderId} after");
    }
}

internal sealed class PlaceOrderHandler : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  handler: placing order {context.OrderId}");
        return Task.CompletedTask;
    }
}
