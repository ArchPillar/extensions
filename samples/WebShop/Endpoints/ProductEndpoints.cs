using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using WebShop.Data;
using WebShop.Mappers;
using WebShop.Models;
using WebShop.Parameters;
using WebShop.Projections;

namespace WebShop.Endpoints;

/// <summary>Product catalogue endpoints.</summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/products").WithTags("Products");

        group.MapGet("/", GetAllAsync)
             .WithSummary("List all active products with flattened category name and availability flag.");

        group.MapGet("/{id:guid}", GetByIdAsync)
             .WithSummary("Get a single product by id.");

        group.MapPost("/", CreateAsync)
             .RequireAuthorization("Admin")
             .WithSummary("Create a new product. [Admin]");

        group.MapPut("/{id:guid}", UpdateAsync)
             .RequireAuthorization("Admin")
             .WithSummary("Update an existing product. [Admin]");

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        List<ProductProjection> products = await db.Products
            .Where(p => p.IsActive)
            .Project(mappers.Product)
            .ToListAsync();

        return Results.Ok(products);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        ProductProjection? product = await db.Products
            .Where(p => p.Id == id)
            .Project(mappers.Product)
            .FirstOrDefaultAsync();

        return product is null ? Results.NotFound() : Results.Ok(product);
    }

    private static async Task<IResult> CreateAsync(
        CreateProductParameters parameters,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == parameters.CategoryId))
        {
            return Results.BadRequest(new { error = "Category not found." });
        }

        Product product = new()
        {
            Id            = Guid.NewGuid(),
            Name          = parameters.Name,
            Description   = parameters.Description,
            Price         = parameters.Price,
            StockQuantity = parameters.StockQuantity,
            CategoryId    = parameters.CategoryId,
            IsActive      = parameters.IsActive,
            CreatedAt     = DateTime.UtcNow,
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        ProductProjection projection = await db.Products
            .Where(p => p.Id == product.Id)
            .Project(mappers.Product)
            .FirstAsync();

        return Results.Created($"/products/{product.Id}", projection);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProductParameters parameters,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        Product? product = await db.Products.FindAsync(id);

        if (product is null)
        {
            return Results.NotFound();
        }

        if (!await db.Categories.AnyAsync(c => c.Id == parameters.CategoryId))
        {
            return Results.BadRequest(new { error = "Category not found." });
        }

        product.Name          = parameters.Name;
        product.Description   = parameters.Description;
        product.Price         = parameters.Price;
        product.StockQuantity = parameters.StockQuantity;
        product.CategoryId    = parameters.CategoryId;
        product.IsActive      = parameters.IsActive;

        await db.SaveChangesAsync();

        ProductProjection projection = await db.Products
            .Where(p => p.Id == id)
            .Project(mappers.Product)
            .FirstAsync();

        return Results.Ok(projection);
    }
}
