using Primitives.WebApiSample.Data;
using Primitives.WebApiSample.Endpoints;

// ---------------------------------------------------------------------------
// Primitives.WebApiSample
//
// A small ASP.NET Core Minimal API showing ArchPillar.Extensions.Operations
// (the OperationResult family) at an HTTP boundary:
//
//   - OperationResult -> IResult via ToProblemResult(), emitting
//     application/problem+json with the status taken straight from
//     OperationStatus — no translation table.
//   - 400 Bad Request carrying field-keyed validation errors.
//   - 404 Not Found for an unknown id.
//   - 201 Created / 200 Ok success paths shaped by the endpoint.
//   - An in-memory, deterministically seeded store — a single dotnet run,
//     no database.
//
// Layered layout: Models/, Requests/, Data/, Infrastructure/, Endpoints/.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<InMemoryProductStore>();

var app = builder.Build();

app.MapProducts();

app.MapGet("/", () => Results.Redirect("/products"));

await app.RunAsync();
