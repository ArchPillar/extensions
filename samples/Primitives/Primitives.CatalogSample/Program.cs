using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Operations;
using Primitives.CatalogSample.Catalog;
using Primitives.CatalogSample.Rendering;
using Spectre.Console;

// ---------------------------------------------------------------------------
// Primitives.CatalogSample
//
// Demonstrates ArchPillar.Extensions.Primitives in an in-memory product catalog:
//   - Success factories: Ok / Created with TValue inferred from the argument.
//   - Failure factories returning OperationFailure, implicitly converted to
//     OperationResult<Product>: NotFound, Conflict, BadRequest with field errors.
//   - The OperationProblem / OperationError shape (type / title / detail, field-
//     keyed Errors, Extensions) rendered with Spectre.Console.
//   - IsSuccess / IsFailure branching; Unwrap() to take the value on success;
//     ThrowIfFailed() to surface a failure as an OperationException.
//   - Implicit conversions: TValue -> OperationResult<TValue>, and
//     throw OperationResult -> OperationException.
//
// Domain types live under Catalog/ — one file per class.
// ---------------------------------------------------------------------------

var catalog = new InMemoryCatalog();

AnsiConsole.MarkupLine("[bold]== happy path ==[/]");

// (1) Valid add -> Created<Product> (TValue inferred).
OperationResult<Product> created = catalog.AddProduct("GADGET-100", "Gadget", 19.50m, 5);
ProblemRenderer.RenderSuccess("AddProduct(GADGET-100)", created, created.Value?.Name);

// (2) Existing get -> Ok, then Unwrap() to trade the result for its value.
OperationResult<Product> found = catalog.GetProduct(InMemoryCatalog.SeededId);
if (found.IsSuccess)
{
    Product widget = found.Unwrap();
    ProblemRenderer.RenderSuccess("GetProduct(seeded)", found, $"{widget.Sku} / {widget.Name}");
}

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[bold]== failure paths ==[/]");

// (3) Missing get -> NotFound; IsFailure branch renders the problem body.
OperationResult<Product> missing = catalog.GetProduct(Id<ProductTag>.New());
if (missing.IsFailure)
{
    ProblemRenderer.RenderProblem("GetProduct(missing)", missing);
}

// (4) Blank name + negative price -> BadRequest with field-keyed errors.
OperationResult<Product> invalid = catalog.AddProduct("BROKEN-001", "  ", -3m, 0);
ProblemRenderer.RenderProblem("AddProduct(blank name, negative price)", invalid);

// (5) Duplicate SKU -> Conflict carrying structured extensions.
OperationResult<Product> duplicate = catalog.AddProduct("WIDGET-001", "Widget Clone", 4.99m, 1);
ProblemRenderer.RenderProblem("AddProduct(duplicate SKU)", duplicate);

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[bold]== ThrowIfFailed -> OperationException ==[/]");

// (6) Take the NotFound result and force it to throw; the exception carries the
// original result, so the handler reads the status straight off it.
try
{
    missing.ThrowIfFailed();
}
catch (OperationException ex)
{
    AnsiConsole.MarkupLine(
        $"  [red]caught[/] OperationException — carried status " +
        $"[yellow]{(int)ex.Result.Status} {ex.Result.Status}[/]");
}
