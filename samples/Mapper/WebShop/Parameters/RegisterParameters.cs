namespace WebShop.Parameters;

/// <summary>Input for registering a new customer account.</summary>
public sealed class RegisterParameters
{
    public required string Email { get; set; }

    public required string Password { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public string? PhoneNumber { get; set; }
}
