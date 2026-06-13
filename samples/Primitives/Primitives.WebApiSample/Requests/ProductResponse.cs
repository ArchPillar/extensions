namespace Primitives.WebApiSample.Requests;

using Primitives.WebApiSample.Models;

/// <summary>
/// The read-side projection returned to callers. Kept separate from the
/// <see cref="Product"/> entity so the wire contract and the domain model can
/// evolve independently.
/// </summary>
public sealed record ProductResponse(Guid Id, string Sku, string Name, decimal Price, int Stock)
{
    public static ProductResponse From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new ProductResponse(product.Id, product.Sku, product.Name, product.Price, product.Stock);
    }
}
