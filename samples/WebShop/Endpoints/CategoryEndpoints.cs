using ArchPillar.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using WebShop.Data;
using WebShop.Mappers;
using WebShop.Models;
using WebShop.Parameters;
using WebShop.Projections;

namespace WebShop.Endpoints;

/// <summary>Product category CRUD endpoints.</summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/categories").WithTags("Categories");

        group.MapGet("/", GetAllAsync)
             .WithSummary("List all categories with their product count.");

        group.MapGet("/{id:guid}", GetByIdAsync)
             .WithSummary("Get a single category by id.");

        group.MapPost("/", CreateAsync)
             .RequireAuthorization("Admin")
             .WithSummary("Create a new category. [Admin]");

        group.MapPut("/{id:guid}", UpdateAsync)
             .RequireAuthorization("Admin")
             .WithSummary("Update an existing category. [Admin]");

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        List<CategoryProjection> categories = await db.Categories
            .Project(mappers.Category)
            .ToListAsync();

        return Results.Ok(categories);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        CategoryProjection? category = await db.Categories
            .Where(c => c.Id == id)
            .Project(mappers.Category)
            .FirstOrDefaultAsync();

        return category is null ? Results.NotFound() : Results.Ok(category);
    }

    private static async Task<IResult> CreateAsync(
        CreateCategoryParameters parameters,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        Category category = new()
        {
            Id          = Guid.NewGuid(),
            Name        = parameters.Name,
            Description = parameters.Description,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync();

        CategoryProjection? projection = await db.Categories
            .Where(c => c.Id == category.Id)
            .Project(mappers.Category)
            .FirstOrDefaultAsync();

        return Results.Created($"/categories/{category.Id}", projection);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateCategoryParameters parameters,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        Category? category = await db.Categories.FindAsync(id);

        if (category is null)
        {
            return Results.NotFound();
        }

        category.Name        = parameters.Name;
        category.Description = parameters.Description;

        await db.SaveChangesAsync();

        // Re-query through the mapper so ProductCount is accurate.
        CategoryProjection? projection = await db.Categories
            .Where(c => c.Id == id)
            .Project(mappers.Category)
            .FirstOrDefaultAsync();

        return Results.Ok(projection);
    }
}
