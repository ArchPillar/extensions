# Mapper.WebShopSample

Demonstrates ArchPillar.Extensions.Mapper in an ASP.NET Core Minimal API over EF Core (SQLite by default, PostgreSQL when configured).

## What it shows
- A `MapperContext` (`WebShopMappers`) holding every `Mapper` as a named property.
- `Project()` translating projections to SQL — flattening (`CategoryName`), computed columns (`IsAvailable`, `LineTotal`), and aggregates (`TotalSpent`).
- A request-scoped `Variable` (`CurrentUserId`) bound per query via `Set()` to compute `IsOwner` inside the SQL projection.
- Optional nested members (`Order.Lines`, `User.Profile`) materialised only when `Include()` opts them in.
- An `EnumMapper` (`OrderStatusCode`) translated to a flat SQL `CASE`, and the EF Core integration (`UseArchPillarMapper`) inlining an enum-mapper call, a single nested mapper, and a collection `Project()` in one hand-written `Select` (the `/orders/summary` endpoint).
- Edge paths: not-found (404) reads, ownership-scoped queries for non-admins, and validation failures on create / place-order (bad category, no lines).

## Running
```bash
dotnet run --project samples/Mapper/Mapper.WebShopSample
```
Starts a web server and prints a panel with the URLs to use: the Scalar UI at `/scalar/v1`, the OpenAPI document at `/openapi/v1.json`, and the token endpoint at `/connect/token`. Obtain a bearer token (`grant_type=password`, `admin@webshop.example` / `Admin123!`), then hit routes such as `GET /products`, `GET /orders`, `GET /orders/summary`, and `GET /orders/{id}`.

## Notes
- The store is an EF Core database (SQLite file `webshop.db` by default; PostgreSQL when a `Postgres` connection string is configured). The schema is created on startup.
- Seeding is opt-in and idempotent — run with `dotnet run --project samples/Mapper/Mapper.WebShopSample -- --seed` once to populate fake data (Bogus); existing data is never overwritten.
- The seeded admin is `admin@webshop.example` / `Admin123!`; seeded customers use the password `Customer123!`.
