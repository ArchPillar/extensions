namespace WebShop.Projections;

/// <summary>
/// Projected view of a product with a flattened category name and a computed availability flag.
/// </summary>
public sealed class ProductProjection
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    /// <summary>Category name flattened from the <c>Category</c> navigation property.</summary>
    public required string CategoryName { get; set; }

    /// <summary>
    /// <c>true</c> when the product is active and has stock — computed as
    /// <c>IsActive &amp;&amp; StockQuantity &gt; 0</c>.
    /// </summary>
    public bool IsAvailable { get; set; }
}
