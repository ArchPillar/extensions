# Commands.WebApiSample

Demonstrates ArchPillar.Extensions.Commands behind HTTP endpoints in an ASP.NET Core Minimal API.

## What it shows
- Endpoints depend on `ICommandDispatcher` and forward the bound command.
- Reads bypass the dispatcher — EF Core projections run directly.
- A user-supplied `TransactionMiddleware` wraps every dispatch.
- Failures map to RFC 7807 problem responses via `OperationResult`.
- Atomic batch create — either all notes succeed or the whole batch is rejected with one problem response.
- Telemetry surfaces every dispatch as a `System.Diagnostics.Activity`.

## Running
```bash
dotnet run --project samples/Commands/Commands.WebApiSample
```
Starts a web server; `/` redirects to `/notes`. Hit `GET /notes` to list, `POST /notes` to create, `PUT /notes/{id}`, `POST /notes/{id}/archive`, and `POST /notes/batch`. In Development the OpenAPI document is served at `/openapi/v1.json`.

## Notes
- The store is a SQLite in-memory database kept open for the app lifetime, so transactions are real (commits and rollbacks observable) and reset each run.
