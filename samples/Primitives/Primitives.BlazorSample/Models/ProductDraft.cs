namespace Primitives.BlazorSample.Models;

/// <summary>
/// A product the user is drafting in the form. Produced as the success payload
/// of <see cref="Validation.ProductDraftValidator"/> once the raw input clears
/// validation.
/// </summary>
/// <param name="Name">The product name. Must be non-empty.</param>
/// <param name="Price">The product price in whole currency units. Must be positive.</param>
public sealed record ProductDraft(string Name, decimal Price);
