namespace WebShop.OData.Projections;

/// <summary>
/// Projected view of an order with computed totals, a flattened customer name,
/// and an optional line collection.
/// </summary>
public sealed class OrderProjection
{
    public Guid Id { get; set; }

    public DateTime PlacedAt { get; set; }

    /// <summary>Human-readable status string.</summary>
    public required string Status { get; set; }

    /// <summary>Sum of all line totals — computed as <c>Lines.Sum(l => l.Quantity * l.UnitPrice)</c>.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Number of distinct lines in the order (computed).</summary>
    public int LineCount { get; set; }

    /// <summary>Full name of the customer — flattened from the <c>Customer</c> navigation property.</summary>
    public required string CustomerFullName { get; set; }

    /// <summary>Optional line details; included only when explicitly requested.</summary>
    public List<OrderLineProjection>? Lines { get; set; }
}
