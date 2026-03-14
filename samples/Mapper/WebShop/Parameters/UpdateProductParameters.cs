namespace WebShop.Parameters;

/// <summary>Input for updating an existing product.</summary>
public sealed class UpdateProductParameters
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public Guid CategoryId { get; set; }

    public bool IsActive { get; set; }
}
