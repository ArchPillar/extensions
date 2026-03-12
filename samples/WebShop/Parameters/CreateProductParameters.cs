namespace WebShop.Parameters;

/// <summary>Input for creating a new product.</summary>
public sealed class CreateProductParameters
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public Guid CategoryId { get; set; }

    public bool IsActive { get; set; } = true;
}
