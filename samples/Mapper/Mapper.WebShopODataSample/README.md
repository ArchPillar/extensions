# Mapper.WebShopODataSample

Demonstrates ArchPillar.Extensions.Mapper behind an ASP.NET Core OData API (Controllers) over EF Core SQLite.

## What it shows
- A `MapperContext` (`WebShopMappers`) holding every `Mapper` as a named property.
- Controllers calling `Project()` to expose projection DTOs as OData entity sets — flattening (`CategoryName`), computed columns (`IsAvailable`), and aggregates (`TotalSpent`, `TotalOrders`).
- `[EnableQuery]` composing `$select` / `$filter` / `$orderby` / `$top` / `$count` / `$expand` on top of the mapper's `IQueryable`, so OData options translate all the way to SQL rather than running in memory.
- Optional nested members (`Order.Lines`) materialised only when `Include()` opts them in (the order-by-key route).

## Running
```bash
dotnet run --project samples/Mapper/Mapper.WebShopODataSample
```
Starts a web server and prints a panel with the OData root (`/odata`), the metadata document (`/odata/$metadata`), and an example query. Hit routes such as `GET /odata/Products?$filter=Price gt 50&$orderby=Name&$top=10`, `GET /odata/Orders(<id>)?$expand=Lines`, and `GET /odata/Customers`.

## Notes
- Targets `net8.0` because `Microsoft.AspNetCore.OData` 9.x does — a tech-demo constraint, not a framework recommendation.
- The store is a SQLite file (`webshop-odata.db` by default); the schema is created on startup.
- Seeding is opt-in and idempotent — run with `dotnet run --project samples/Mapper/Mapper.WebShopODataSample -- --seed` once to populate fake data (Bogus); existing data is never overwritten.
