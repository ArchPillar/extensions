using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Operations;

namespace Primitives.CatalogSample.Catalog;

// A deterministic in-memory store. Seeded ids are fixed Guids so console output is
// identical run to run. Every method returns an OperationResult / OperationResult<T>
// so callers branch on status rather than on null/exception sentinels.
internal sealed class InMemoryCatalog
{
    private static readonly Id<ProductTag> SeededWidgetId =
        new(new Guid("00000000-0000-0000-0000-000000000001"));

    private readonly Dictionary<Id<ProductTag>, Product> _products = [];

    public InMemoryCatalog()
    {
        var widget = new Product(SeededWidgetId, "WIDGET-001", "Widget", 9.99m, 12);
        _products[widget.Id] = widget;
    }

    public static Id<ProductTag> SeededId => SeededWidgetId;

    // The implicit OperationFailure -> OperationResult<Product> conversion is what
    // lets NotFound be returned from a Product-typed signature without restating it.
    public OperationResult<Product> GetProduct(Id<ProductTag> id)
    {
        if (_products.TryGetValue(id, out Product? product))
        {
            return OperationResult.Ok(product);
        }

        return OperationResult.NotFound($"Product '{id}' was not found.");
    }

    public OperationResult<Product> AddProduct(string sku, string name, decimal price, int stock)
    {
        if (ProductValidator.Validate(name, price) is { } failure)
        {
            return failure;
        }

        if (_products.Values.Any(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Conflict(
                $"A product with SKU '{sku}' already exists.",
                extensions: new Dictionary<string, object?>
                {
                    ["sku"] = sku,
                });
        }

        var product = new Product(Id<ProductTag>.New(), sku, name, price, stock);
        _products[product.Id] = product;
        return OperationResult.Created(product);
    }
}
