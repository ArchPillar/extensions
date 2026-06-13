using ArchPillar.Extensions.Operations;
using Primitives.WebApiSample.Data;
using Primitives.WebApiSample.Infrastructure;
using Primitives.WebApiSample.Models;
using Primitives.WebApiSample.Requests;

namespace Primitives.WebApiSample.Endpoints;

/// <summary>Product catalogue endpoints over the in-memory store.</summary>
internal static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProducts(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/products").WithTags("Products");

        // Reads return data directly — a query that finds nothing is a 404
        // shaped by the endpoint, not a store-level failure.
        group.MapGet("/", (InMemoryProductStore store) =>
        {
            var products = store.GetAll()
                .Select(ProductResponse.From)
                .ToList();
            return Results.Ok(products);
        });

        group.MapGet("/{id:guid}", (Guid id, InMemoryProductStore store) =>
        {
            Product? product = store.Find(id);
            return product is null
                ? OperationResult.NotFound($"No product with id '{id}'.").ToProblemResult()
                : Results.Ok(ProductResponse.From(product));
        });

        // Writes validate first, then hand off to the store. Both stages return
        // an OperationResult whose status maps straight onto the HTTP response:
        // 400 with field errors, 409 on a duplicate SKU, 201 on success.
        group.MapPost("/", (CreateProductRequest request, InMemoryProductStore store) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            OperationResult validation = ProductValidator.Validate(request);
            if (validation.IsFailure)
            {
                return validation.ToProblemResult();
            }

            OperationResult<Product> created = store.Create(
                request.Sku!,
                request.Name!,
                request.Price,
                request.Stock);

            return created.ToResult(product =>
                Results.Created($"/products/{product.Id}", ProductResponse.From(product)));
        });

        return routes;
    }
}
