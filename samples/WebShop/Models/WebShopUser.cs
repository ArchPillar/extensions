using Microsoft.AspNetCore.Identity;

namespace WebShop.Models;

/// <summary>Application user extending ASP.NET Core Identity.</summary>
public sealed class WebShopUser : IdentityUser<Guid>
{
    /// <summary>Role of the user: <c>Admin</c> or <c>Customer</c>.</summary>
    public required string Role { get; set; }

    /// <summary>UTC timestamp when the account was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Customer profile; only present when <see cref="Role"/> is <c>Customer</c>.</summary>
    public Customer? Customer { get; set; }
}
