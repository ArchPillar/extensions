# ArchPillar.Extensions.Primitives.EntityFrameworkCore

EF Core conventions for the strongly-typed identifiers in
[`ArchPillar.Extensions.Primitives`](https://www.nuget.org/packages/ArchPillar.Extensions.Primitives).
Opt in once per `DbContext` and every `Id<T>` property persists as a `Guid`/`uuid`,
round-trips through change tracking, and translates inside arbitrary LINQ queries — no
per-property configuration.

## Why?

`Id<T>` is a phantom-typed `readonly struct` wrapping a `Guid`, so an `Id<Order>` can never be
assigned to an `Id<Customer>`. The core Primitives package keeps that type BCL-only and free
of any EF Core dependency. Persisting it, though, needs a value converter, a value comparer,
and — to keep `Where` / `Join` translating server-side — a relational type-mapping plugin.
This package supplies all three behind a single opt-in call, so typed ids cost you nothing at
the model level once registered.

## Quick Start

Add the package, then enable the convention when configuring the context options:

```bash
dotnet add package ArchPillar.Extensions.Primitives.EntityFrameworkCore
```

```csharp
using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Models.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class Order
{
    public Id<Order> Id { get; set; }
    public Id<Customer>? CustomerId { get; set; }   // nullable typed ids handled too
}

// Program.cs
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        .UseArchPillarTypedIds());
```

With the convention active, typed ids translate the way the underlying `Guid` would:

```csharp
Id<Order> id = Id<Order>.New();

Order? order = await db.Orders
    .Where(o => o.Id == id)        // translated server-side; Id<Order> maps to uuid
    .FirstOrDefaultAsync();
```

## Convention surface

`UseArchPillarTypedIds()` registers, for the model:

- a `ValueConverter<Id<T>, Guid>` and `ValueComparer<Id<T>>` on every property whose CLR type
  is `Id<T>` (or `Id<T>?`) and that does not already have a converter, and
- a relational type-mapping plugin so `Id<T>` is a first-class SQL type in query translation.

The call is idempotent, and a generic `UseArchPillarTypedIds<TContext>()` overload chains
alongside typed builder extensions such as `UseNpgsql<TContext>()`.

## Per-property opt-in

When the global convention is not active, or you need surgical control, configure one property
in `OnModelCreating`. A nullable overload handles `Id<T>?`.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>(entity =>
    {
        entity.Property(o => o.Id).HasIdConversion();
        entity.Property(o => o.CustomerId).HasIdConversion();   // nullable overload
    });
}
```

## Documentation

This is an integration of Primitives — its full documentation lives with the parent library,
under [docs/primitives/](https://github.com/ArchPillar/extensions/tree/main/docs/primitives),
specifically the
[EF Core integration feature](https://github.com/ArchPillar/extensions/blob/main/docs/primitives/features.md#ef-core-integration-archpillarextensionsprimitivesentityframeworkcore).

## License

MIT — see the bundled `LICENSE` file.
