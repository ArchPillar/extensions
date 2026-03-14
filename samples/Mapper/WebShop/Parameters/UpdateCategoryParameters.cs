namespace WebShop.Parameters;

/// <summary>Input for updating an existing product category.</summary>
public sealed class UpdateCategoryParameters
{
    public required string Name { get; set; }

    public string? Description { get; set; }
}
