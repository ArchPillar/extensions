using ArchPillar.Extensions.Operations;

namespace Primitives.CatalogSample.Catalog;

// Validation is its own step so the store stays a pure persistence concern. The
// validator builds the RFC 7807 field-keyed Errors dictionary by hand to show the
// shape end-to-end — one OperationError per failing rule, keyed by field name.
internal static class ProductValidator
{
    private const decimal MinPrice = 0m;

    // Returns null when the input is valid; the caller proceeds only on null.
    public static OperationFailure? Validate(string name, decimal price)
    {
        var errors = new Dictionary<string, IReadOnlyList<OperationError>>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] =
            [
                new OperationError(
                    Type: "required",
                    Detail: "name is required.",
                    Status: OperationStatus.BadRequest),
            ];
        }

        if (price < MinPrice)
        {
            errors["price"] =
            [
                new OperationError(
                    Type: "out_of_range",
                    Detail: $"price must be at least {MinPrice}.",
                    Status: OperationStatus.BadRequest,
                    Extensions: new Dictionary<string, object?>
                    {
                        ["min"] = MinPrice,
                        ["actual"] = price,
                    }),
            ];
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return OperationResult.BadRequest(
            "One or more validation errors occurred.",
            errors: errors);
    }
}
