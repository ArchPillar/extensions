using ArchPillar.Extensions.Operations;
using Primitives.WebApiSample.Requests;

namespace Primitives.WebApiSample.Data;

/// <summary>
/// Validates a <see cref="CreateProductRequest"/> and collects every field
/// failure into one problem. Field-keyed errors let the caller see all the
/// problems with their input at once rather than one per round-trip.
/// </summary>
internal static class ProductValidator
{
    public static OperationResult Validate(CreateProductRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, IReadOnlyList<OperationError>>();

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            errors[nameof(request.Sku)] =
                [new OperationError("required", "SKU is required.", OperationStatus.BadRequest)];
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] =
                [new OperationError("required", "Name is required.", OperationStatus.BadRequest)];
        }

        if (request.Price < 0)
        {
            errors[nameof(request.Price)] =
            [
                new OperationError(
                    "out_of_range",
                    "Price must not be negative.",
                    OperationStatus.BadRequest,
                    new Dictionary<string, object?> { ["min"] = 0m, ["actual"] = request.Price }),
            ];
        }

        if (request.Stock < 0)
        {
            errors[nameof(request.Stock)] =
            [
                new OperationError(
                    "out_of_range",
                    "Stock must not be negative.",
                    OperationStatus.BadRequest,
                    new Dictionary<string, object?> { ["min"] = 0, ["actual"] = request.Stock }),
            ];
        }

        if (errors.Count > 0)
        {
            return OperationResult.BadRequest("The product request is invalid.", errors: errors);
        }

        return OperationResult.Ok();
    }
}
