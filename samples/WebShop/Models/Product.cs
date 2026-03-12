namespace WebShop.Models;

/// <summary>Purchasable product in the catalogue.</summary>
public sealed class Product
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<OrderLine> OrderLines { get; set; } = [];
}
