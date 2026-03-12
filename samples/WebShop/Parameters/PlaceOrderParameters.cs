namespace WebShop.Parameters;

/// <summary>Input for placing a new order.</summary>
public sealed class PlaceOrderParameters
{
    public required string ShippingAddress { get; set; }

    public required List<OrderLineParameters> Lines { get; set; }
}

/// <summary>A single line within a <see cref="PlaceOrderParameters"/>.</summary>
public sealed class OrderLineParameters
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; }
}
