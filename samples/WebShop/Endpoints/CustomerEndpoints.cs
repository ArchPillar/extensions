using ArchPillar.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using WebShop.Data;
using WebShop.Mappers;
using WebShop.Projections;

namespace WebShop.Endpoints;

/// <summary>Customer management endpoints (Admin-facing).</summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/customers")
                                     .WithTags("Customers")
                                     .RequireAuthorization("Admin");

        group.MapGet("/", GetAllAsync)
             .WithSummary("List all customers with computed order statistics. [Admin]");

        group.MapGet("/{id:guid}", GetByIdAsync)
             .WithSummary("Get a single customer by id. [Admin]");

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        List<CustomerProjection> customers = await db.Customers
            .Project(mappers.Customer)
            .ToListAsync();

        return Results.Ok(customers);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        CustomerProjection? customer = await db.Customers
            .Where(c => c.Id == id)
            .Project(mappers.Customer)
            .FirstOrDefaultAsync();

        return customer is null ? Results.NotFound() : Results.Ok(customer);
    }
}
