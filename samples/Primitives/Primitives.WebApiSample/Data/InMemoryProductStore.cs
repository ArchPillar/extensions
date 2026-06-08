using ArchPillar.Extensions.Operations;
using Primitives.WebApiSample.Models;

namespace Primitives.WebApiSample.Data;

/// <summary>
/// A deterministically seeded in-memory product catalogue. Registered as a
/// singleton so it survives across requests and resets every run — no database.
/// </summary>
/// <remarks>
/// Writes return an <see cref="OperationResult"/> carrying an HTTP-aligned
/// status (Created / NotFound / Conflict). Reads return data directly: a query
/// that finds nothing is a normal answer, not a failure.
/// </remarks>
internal sealed class InMemoryProductStore
{
    private static readonly Guid SeedKeyboardId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeedMouseId = new("22222222-2222-2222-2222-222222222222");

    private readonly Dictionary<Guid, Product> _products = [];
    private readonly Lock _gate = new();

    public InMemoryProductStore()
    {
        Seed();
    }

    public IReadOnlyList<Product> GetAll()
    {
        lock (_gate)
        {
            return [.. _products.Values];
        }
    }

    public Product? Find(Guid id)
    {
        lock (_gate)
        {
            return _products.GetValueOrDefault(id);
        }
    }

    public OperationResult<Product> Create(string sku, string name, decimal price, int stock)
    {
        lock (_gate)
        {
            if (_products.Values.Any(existing => string.Equals(existing.Sku, sku, StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult.Conflict($"A product with SKU '{sku}' already exists.");
            }

            var product = new Product
            {
                Id    = Guid.NewGuid(),
                Sku   = sku,
                Name  = name,
                Price = price,
                Stock = stock,
            };

            _products[product.Id] = product;
            return OperationResult.Created(product);
        }
    }

    public OperationResult Restock(Guid id, int quantity)
    {
        lock (_gate)
        {
            if (!_products.TryGetValue(id, out Product? product))
            {
                return OperationResult.NotFound($"No product with id '{id}'.");
            }

            product.Stock += quantity;
            return OperationResult.NoContent();
        }
    }

    private void Seed()
    {
        _products[SeedKeyboardId] = new Product
        {
            Id    = SeedKeyboardId,
            Sku   = "KBD-001",
            Name  = "Mechanical Keyboard",
            Price = 89.90m,
            Stock = 25,
        };

        _products[SeedMouseId] = new Product
        {
            Id    = SeedMouseId,
            Sku   = "MOU-001",
            Name  = "Wireless Mouse",
            Price = 39.50m,
            Stock = 60,
        };
    }
}
