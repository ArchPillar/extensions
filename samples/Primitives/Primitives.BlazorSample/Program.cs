// ---------------------------------------------------------------------------
// Primitives.BlazorSample
//
// Demonstrates ArchPillar.Extensions.Primitives in a Blazor WebAssembly client:
//   - A form component validates input and produces OperationResult<ProductDraft>
//     (Ok on success, BadRequest with field-keyed errors on failure).
//   - Rendering the contrast in the UI: a success value versus the
//     OperationProblem / OperationError shape that describes a failure.
//   - Deserializing a canned application/problem+json payload into an
//     OperationProblem with System.Text.Json and rendering it — the result
//     round-tripping from the wire into the same display.
//   - Trim-safe / AOT-friendly, BCL-only Primitives: no server dependency, the
//     whole sample runs client-side from a single dotnet run.
//
// Components live under Pages/ and Shared/; validation lives under Validation/.
// ---------------------------------------------------------------------------

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Primitives.BlazorSample;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
