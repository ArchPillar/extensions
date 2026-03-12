namespace WebShop.Models;

/// <summary>A placed customer order.</summary>
public sealed class Order
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public DateTime PlacedAt { get; set; }

    public OrderStatus Status { get; set; }

    /// <summary>Shipping address snapshot captured at order placement time.</summary>
    public required string ShippingAddress { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = [];
}
