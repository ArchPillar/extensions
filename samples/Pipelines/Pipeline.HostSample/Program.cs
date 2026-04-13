using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------
// Pipeline.HostSample
//
// Demonstrates using Pipeline<T> inside a Microsoft.Extensions.Hosting app
// with full DI wiring. Middlewares and the handler are classes with
// constructor-injected dependencies (here: ILogger<T>), registered via
// services.AddPipeline<OrderContext>()... and resolved from the container.
// ---------------------------------------------------------------------------

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services
    .AddPipeline<OrderContext>()
    .Use<LoggingMiddleware>()
    .Use<ValidationMiddleware>()
    .Use<AuthorizationMiddleware>()
    .Handle<PlaceOrderHandler>();

using IHost host = builder.Build();

using (IServiceScope scope = host.Services.CreateScope())
{
    Pipeline<OrderContext> pipeline = scope.ServiceProvider.GetRequiredService<Pipeline<OrderContext>>();

    Console.WriteLine("== happy path ==");
    await pipeline.ExecuteAsync(new OrderContext { OrderId = 42, UserId = 7 });

    Console.WriteLine();
    Console.WriteLine("== fails validation ==");
    await pipeline.ExecuteAsync(new OrderContext { OrderId = 0, UserId = 7 });

    Console.WriteLine();
    Console.WriteLine("== fails authorization ==");
    await pipeline.ExecuteAsync(new OrderContext { OrderId = 99, UserId = 0 });
}

// ---------------------------------------------------------------------------
// Context, middlewares, handler
// ---------------------------------------------------------------------------

internal sealed class OrderContext
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public bool Authorized { get; set; }
}

internal sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("starting order {OrderId}", context.OrderId);
        try
        {
            await next(context, cancellationToken);
            logger.LogInformation("completed order {OrderId}", context.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed order {OrderId}", context.OrderId);
            throw;
        }
    }
}

internal sealed class ValidationMiddleware(ILogger<ValidationMiddleware> logger) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken = default)
    {
        if (context.OrderId <= 0)
        {
            logger.LogWarning("validation failed: invalid OrderId");
            return; // short-circuit: skip later middlewares and the handler
        }

        await next(context, cancellationToken);
    }
}

internal sealed class AuthorizationMiddleware(ILogger<AuthorizationMiddleware> logger) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken = default)
    {
        if (context.UserId <= 0)
        {
            logger.LogWarning("authorization failed: anonymous user");
            return;
        }

        context.Authorized = true;
        await next(context, cancellationToken);
    }
}

internal sealed class PlaceOrderHandler(ILogger<PlaceOrderHandler> logger) : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "placing order {OrderId} for user {UserId} (authorized={Authorized})",
            context.OrderId,
            context.UserId,
            context.Authorized);
        return Task.CompletedTask;
    }
}
