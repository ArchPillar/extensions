namespace WebShop.Projections;

/// <summary>Projected view of a category, including a computed product count.</summary>
public sealed class CategoryProjection
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Number of products belonging to this category (computed).</summary>
    public int ProductCount { get; set; }
}
