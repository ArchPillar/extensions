using System.Security.Claims;
using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using WebShop.Data;
using WebShop.Mappers;
using WebShop.Models;
using WebShop.Parameters;
using WebShop.Projections;

namespace WebShop.Endpoints;

/// <summary>Order management endpoints for authenticated customers.</summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/orders")
                                     .WithTags("Orders")
                                     .RequireAuthorization();

        // List own orders (summary, no line details).
        group.MapGet("/", GetMyOrdersAsync)
             .WithSummary("List the current customer's orders. IsOwner is always true here.");

        // Get a single order with lines included.
        group.MapGet("/{id:guid}", GetByIdAsync)
             .WithSummary("Get order details including line items. IsOwner reflects ownership.");

        // Place a new order.
        group.MapPost("/", PlaceOrderAsync)
             .WithSummary("Place a new order.");

        // Cancel a pending order.
        group.MapPost("/{id:guid}/cancel", CancelOrderAsync)
             .WithSummary("Cancel a pending or processing order.");

        // Admin: list all orders for any customer.
        app.MapGet("/customers/{customerId:guid}/orders", GetCustomerOrdersAsync)
           .WithTags("Orders")
           .RequireAuthorization("Admin")
           .WithSummary("List all orders for a customer. [Admin]");

        return app;
    }

    private static async Task<IResult> GetMyOrdersAsync(
        ClaimsPrincipal principal,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        Guid userId = GetUserId(principal);

        List<OrderProjection> orders = await db.Orders
            .Where(o => o.Customer.UserId == userId)
            .Project(mappers.Order, opts => opts.Set(mappers.CurrentUserId, userId))
            .ToListAsync();

        return Results.Ok(orders);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ClaimsPrincipal principal,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        Guid userId  = GetUserId(principal);
        var isAdmin = principal.FindFirstValue("role") == "Admin";

        IQueryable<Order> query = db.Orders.Where(o => o.Id == id);

        if (!isAdmin)
        {
            query = query.Where(o => o.Customer.UserId == userId);
        }

        OrderProjection? order = await query
            .Project(mappers.Order, opts => opts
                .Set(mappers.CurrentUserId, userId)
                .Include(o => o.Lines))
            .FirstOrDefaultAsync();

        return order is null ? Results.NotFound() : Results.Ok(order);
    }

    private static async Task<IResult> PlaceOrderAsync(
        PlaceOrderParameters parameters,
        ClaimsPrincipal principal,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        Guid userId        = GetUserId(principal);
        Customer? customer = await db.Customers.FirstOrDefaultAsync(c => c.UserId == userId);

        if (customer is null)
        {
            return Results.Forbid();
        }

        if (parameters.Lines.Count == 0)
        {
            return Results.BadRequest(new { error = "An order must have at least one line." });
        }

        var productIds = parameters.Lines.Select(l => l.ProductId).Distinct().ToList();

        List<Models.Product> products = await db.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        if (products.Count != productIds.Count)
        {
            return Results.BadRequest(new { error = "One or more products are unavailable." });
        }

        var order = new Order
        {
            Id              = Guid.NewGuid(),
            CustomerId      = customer.Id,
            PlacedAt        = DateTime.UtcNow,
            Status          = OrderStatus.Pending,
            ShippingAddress = parameters.ShippingAddress,
        };

        foreach (OrderLineParameters lineParam in parameters.Lines)
        {
            Product product = products.First(p => p.Id == lineParam.ProductId);
            order.Lines.Add(new OrderLine
            {
                Id          = Guid.NewGuid(),
                ProductId   = product.Id,
                ProductName = product.Name,
                UnitPrice   = product.Price,
                Quantity    = lineParam.Quantity,
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        OrderProjection projection = await db.Orders
            .Where(o => o.Id == order.Id)
            .Project(mappers.Order, opts => opts
                .Set(mappers.CurrentUserId, userId)
                .Include(o => o.Lines))
            .FirstAsync();

        return Results.Created($"/orders/{order.Id}", projection);
    }

    private static async Task<IResult> CancelOrderAsync(
        Guid id,
        ClaimsPrincipal principal,
        WebShopDbContext db)
    {
        Guid userId  = GetUserId(principal);
        var isAdmin = principal.FindFirstValue("role") == "Admin";

        IQueryable<Order> query = db.Orders.Where(o => o.Id == id);

        if (!isAdmin)
        {
            query = query.Where(o => o.Customer.UserId == userId);
        }

        Order? order = await query.FirstOrDefaultAsync();

        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Processing))
        {
            return Results.BadRequest(new { error = "Only pending or processing orders can be cancelled." });
        }

        order.Status = OrderStatus.Cancelled;
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> GetCustomerOrdersAsync(
        Guid customerId,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId))
        {
            return Results.NotFound();
        }

        // For admin views IsOwner is always false — pass Guid.Empty so the variable is bound.
        List<OrderProjection> orders = await db.Orders
            .Where(o => o.CustomerId == customerId)
            .Project(mappers.Order, opts => opts.Set(mappers.CurrentUserId, Guid.Empty))
            .ToListAsync();

        return Results.Ok(orders);
    }

    private static Guid GetUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject)!);
}
