namespace Primitives.WebApiSample.Requests;

/// <summary>
/// The POST body for creating a product. A wire-shape DTO — fields are
/// nullable so a missing or blank value reaches the validator rather than
/// failing model binding.
/// </summary>
public sealed record CreateProductRequest(string? Sku, string? Name, decimal Price, int Stock);
