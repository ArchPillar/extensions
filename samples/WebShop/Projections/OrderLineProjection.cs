namespace WebShop.Projections;

/// <summary>Projected view of an order line with a computed line total.</summary>
public sealed class OrderLineProjection
{
    public Guid Id { get; set; }

    public required string ProductName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>Computed as <c>Quantity * UnitPrice</c>.</summary>
    public decimal LineTotal { get; set; }
}
