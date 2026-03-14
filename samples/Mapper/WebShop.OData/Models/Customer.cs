namespace WebShop.OData.Models;

/// <summary>Customer profile.</summary>
public sealed class Customer
{
    public Guid Id { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public required string Email { get; set; }

    public string? PhoneNumber { get; set; }

    /// <summary>Default shipping address for new orders.</summary>
    public string? ShippingAddress { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
}
