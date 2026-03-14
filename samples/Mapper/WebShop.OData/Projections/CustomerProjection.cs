namespace WebShop.OData.Projections;

/// <summary>
/// Projected view of a customer with a computed full name and order statistics.
/// </summary>
public sealed class CustomerProjection
{
    public Guid Id { get; set; }

    /// <summary>Computed as <c>FirstName + " " + LastName</c>.</summary>
    public required string FullName { get; set; }

    public required string Email { get; set; }

    public string? PhoneNumber { get; set; }

    /// <summary>Total number of placed orders (computed).</summary>
    public int TotalOrders { get; set; }

    /// <summary>
    /// Sum of all order line totals across all orders — computed as
    /// <c>Orders.Sum(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice))</c>.
    /// </summary>
    public decimal TotalSpent { get; set; }
}
