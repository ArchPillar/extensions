using ArchPillar.Extensions.Operations;
using Primitives.BlazorSample.Models;

namespace Primitives.BlazorSample.Validation;

/// <summary>
/// Validates raw product form input into a <see cref="ProductDraft"/>. Pure C#
/// with no host coupling — the same call works in the browser, a console, or a
/// server. On success it returns <see cref="OperationResult.Ok{TValue}(TValue)"/>;
/// on failure a <see cref="OperationStatus.BadRequest"/> carrying field-keyed
/// <see cref="OperationError"/> items.
/// </summary>
public static class ProductDraftValidator
{
    /// <summary>
    /// Validates <paramref name="name"/> and <paramref name="price"/> and
    /// produces a <see cref="ProductDraft"/> when both clear.
    /// </summary>
    /// <param name="name">The raw product name from the form.</param>
    /// <param name="price">The raw product price from the form.</param>
    /// <returns>
    /// A successful result carrying the draft, or a
    /// <see cref="OperationStatus.BadRequest"/> failure whose
    /// <see cref="OperationProblem.Errors"/> are keyed by field name.
    /// </returns>
    public static OperationResult<ProductDraft> Validate(string? name, decimal price)
    {
        var errors = new Dictionary<string, IReadOnlyList<OperationError>>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(name))
        {
            errors[nameof(ProductDraft.Name)] =
            [
                new OperationError(
                    Type: "required",
                    Detail: "Name is required.",
                    Status: OperationStatus.BadRequest),
            ];
        }

        if (price <= 0)
        {
            errors[nameof(ProductDraft.Price)] =
            [
                new OperationError(
                    Type: "out_of_range",
                    Detail: "Price must be greater than zero.",
                    Status: OperationStatus.BadRequest,
                    Extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["min"] = 0,
                        ["actual"] = price,
                    }),
            ];
        }

        if (errors.Count > 0)
        {
            return OperationResult.BadRequest(
                detail: "The product draft failed validation.",
                errors: errors);
        }

        return OperationResult.Ok(new ProductDraft(name!.Trim(), price));
    }
}
