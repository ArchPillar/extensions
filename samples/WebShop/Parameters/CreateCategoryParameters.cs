namespace WebShop.Parameters;

/// <summary>Input for creating a new product category.</summary>
public sealed class CreateCategoryParameters
{
    public required string Name { get; set; }

    public string? Description { get; set; }
}
