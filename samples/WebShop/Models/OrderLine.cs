namespace WebShop.Models;

/// <summary>A single line within an <see cref="Order"/>.</summary>
public sealed class OrderLine
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Product name snapshot captured at order placement time.</summary>
    public required string ProductName { get; set; }

    /// <summary>Unit price snapshot captured at order placement time.</summary>
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }
}
