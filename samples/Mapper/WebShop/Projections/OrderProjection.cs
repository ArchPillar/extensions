namespace WebShop.Projections;

/// <summary>
/// Projected view of an order with computed totals, a flattened customer name,
/// an ownership flag bound to the <c>CurrentUserId</c> variable, and an optional
/// line collection.
/// </summary>
public sealed class OrderProjection
{
    public Guid Id { get; set; }

    public DateTime PlacedAt { get; set; }

    /// <summary>Human-readable status string produced by the enum mapper.</summary>
    public required string Status { get; set; }

    /// <summary>Sum of all line totals — computed as <c>Lines.Sum(l =&gt; l.Quantity * l.UnitPrice)</c>.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Number of distinct lines in the order (computed).</summary>
    public int LineCount { get; set; }

    /// <summary>Full name of the customer — flattened from the <c>Customer</c> navigation property.</summary>
    public required string CustomerFullName { get; set; }

    /// <summary>
    /// <c>true</c> when the order belongs to the current user.
    /// Bound to the <c>CurrentUserId</c> runtime variable.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>Optional line details; included only when explicitly requested.</summary>
    public List<OrderLineProjection>? Lines { get; set; }
}
