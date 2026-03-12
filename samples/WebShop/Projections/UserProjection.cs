namespace WebShop.Projections;

/// <summary>
/// Projected view of a user with an optional nested customer profile.
/// </summary>
public sealed class UserProjection
{
    public Guid Id { get; set; }

    public required string? Email { get; set; }

    public required string Role { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Customer profile — included only when explicitly requested.</summary>
    public CustomerProjection? Profile { get; set; }
}
