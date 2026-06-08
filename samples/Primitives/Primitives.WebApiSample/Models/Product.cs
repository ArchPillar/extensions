namespace Primitives.WebApiSample.Models;

/// <summary>
/// A product in the catalogue. The mutable domain entity held by the store —
/// distinct from the read-side <c>ProductResponse</c> returned to callers.
/// </summary>
public sealed class Product
{
    public Guid Id { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }
}
