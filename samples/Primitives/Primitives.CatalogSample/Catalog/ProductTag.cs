namespace Primitives.CatalogSample.Catalog;

// Phantom marker for Id<ProductTag>: it is never instantiated. Its only job is
// to make Id<ProductTag> a distinct type the compiler refuses to confuse with
// any other entity's id.
internal sealed class ProductTag;
