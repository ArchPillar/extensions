using ArchPillar.Extensions.Models;

namespace Primitives.CatalogSample.Catalog;

// Id is an Id<ProductTag>, not a raw Guid, so it can never be passed where some
// other entity's id is expected.
internal sealed record Product(
    Id<ProductTag> Id,
    string Sku,
    string Name,
    decimal Price,
    int Stock);
