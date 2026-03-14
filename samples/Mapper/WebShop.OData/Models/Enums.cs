namespace WebShop.OData.Models;

/// <summary>Lifecycle status of an order.</summary>
public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
}
