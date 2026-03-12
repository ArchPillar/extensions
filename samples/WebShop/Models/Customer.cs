namespace WebShop.Models;

/// <summary>Customer profile linked to a <see cref="WebShopUser"/>.</summary>
public sealed class Customer
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public WebShopUser User { get; set; } = null!;

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public string? PhoneNumber { get; set; }

    /// <summary>Default shipping address for new orders.</summary>
    public string? ShippingAddress { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
}
