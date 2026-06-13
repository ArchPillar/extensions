namespace Mapper.WebShopSample.Models;

/// <summary>
/// API-facing order status code. Deliberately uses different numeric values from
/// <see cref="OrderStatus"/> so the enum mapper produces a real SQL <c>CASE</c>
/// rather than a no-op cast.
/// </summary>
public enum OrderStatusDto
{
    Unknown = 0,
    Open = 10,
    InProgress = 20,
    Dispatched = 30,
    Completed = 40,
    Voided = 50,
}
