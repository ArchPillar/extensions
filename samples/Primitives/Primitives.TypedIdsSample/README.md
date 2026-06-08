# Primitives.TypedIdsSample

A console app that persists phantom-typed `Id<T>` identifiers to SQLite through the opt-in `ArchPillar.Extensions.Primitives.EntityFrameworkCore` add-on.

## What it shows
- `UseArchPillarTypedIds()` auto-converts every `Id<T>` property to its `Guid` column — the `User`/`Order` keys and the `Order.OwnerId` FK — with no per-property config.
- One explicit `Property(...).HasIdConversion()` for the nullable `User.LatestOrderId`, demonstrating the per-property path.
- An insert + query round-trip where a LINQ lookup by a typed id is translated to SQL.
- Operations return `OperationResult` — `Ok` on a hit, `NotFound` for a missing id.

## Running
```bash
dotnet run --project samples/Primitives/Primitives.TypedIdsSample
```
Prints the user round-trip, the order looked up by its owner's typed id, a `NotFound` for an id that was never stored, and the reloaded nullable `LatestOrderId`.

## Notes
The store is a SQLite `:memory:` database whose connection is held open for the app lifetime, reseeded from scratch on each run, so output is stable.
