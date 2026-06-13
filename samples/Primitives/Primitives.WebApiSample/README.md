# Primitives.WebApiSample

Demonstrates the `ArchPillar.Extensions.Operations` result family at an HTTP boundary in an ASP.NET Core Minimal API.

## What it shows
- `OperationResult` maps to `IResult` via `ToProblemResult()`, emitting `application/problem+json` with the status taken straight from `OperationStatus`.
- `400 Bad Request` carrying field-keyed validation errors.
- `404 Not Found` for an unknown id.
- `201 Created` / `200 Ok` success paths shaped by the endpoint.
- An in-memory, deterministically seeded store — a single `dotnet run`, no database.

## Running
```bash
dotnet run --project samples/Primitives/Primitives.WebApiSample
```
Starts a web server (http://localhost:5110); `/` redirects to `/products`. Routes:

- `GET /products` — list all products (200).
- `GET /products/{id}` — a single product (200), or a `problem+json` body on 404 for an unknown id.
- `POST /products` — create a product. A valid body returns 201 with a `Location` header; a blank name or negative price returns a 400 `problem+json` body with field-keyed `errors`; a duplicate SKU returns a 409 `problem+json` body.

## Notes
- The store is in-memory and reseeded each run, so output is stable run to run and nothing persists.
