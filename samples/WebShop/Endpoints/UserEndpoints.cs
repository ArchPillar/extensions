using System.Security.Claims;
using ArchPillar.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using WebShop.Data;
using WebShop.Mappers;
using WebShop.Projections;

namespace WebShop.Endpoints;

/// <summary>User account endpoints.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/users").WithTags("Users");

        // Current user profile — includes the customer profile.
        group.MapGet("/me", GetMeAsync)
             .RequireAuthorization()
             .WithSummary("Get the current user's profile including customer details.");

        // Admin: list all users.
        group.MapGet("/", GetAllAsync)
             .RequireAuthorization("Admin")
             .WithSummary("List all users. [Admin]");

        // Admin: get a specific user.
        group.MapGet("/{id:guid}", GetByIdAsync)
             .RequireAuthorization("Admin")
             .WithSummary("Get a user by id. [Admin]");

        return app;
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        var userId = Guid.Parse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject)!);

        UserProjection? user = await db.Users
            .Where(u => u.Id == userId)
            .Project(mappers.User, opts => opts.Include(u => u.Profile))
            .FirstOrDefaultAsync();

        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> GetAllAsync(
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        List<UserProjection> users = await db.Users
            .Project(mappers.User)
            .ToListAsync();

        return Results.Ok(users);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        WebShopDbContext db,
        WebShopMappers mappers)
    {
        UserProjection? user = await db.Users
            .Where(u => u.Id == id)
            .Project(mappers.User, opts => opts.Include(u => u.Profile))
            .FirstOrDefaultAsync();

        return user is null ? Results.NotFound() : Results.Ok(user);
    }
}
