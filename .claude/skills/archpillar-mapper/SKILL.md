---
name: archpillar-mapper
description: >-
  Write and review object-to-object / DTO mapping and LINQ-EF Core projection code with
  ArchPillar.Extensions.Mapper — the explicit, expression-tree-based .NET mapping library
  built around MapperContext, Mapper<TSource,TDest>, Map(), and Project(). Use whenever a
  project references ArchPillar.Extensions.Mapper and you are authoring or editing mappers,
  DTO projections, MapperContext definitions, enum mappings, or EF Core projections — and
  use it INSTEAD of reaching for AutoMapper-style conventions, profiles, or attributes.
  Covers the explicit-mapping rules, EF Core-translatable expression constraints, optional
  properties, runtime variables, enum mapping, MapTo, inheritance, and DI setup.
---

# ArchPillar.Extensions.Mapper

A lightweight .NET mapping library built on expression trees. **One mapper definition drives
both in-memory object mapping and LINQ/EF Core projection.** It is deliberately the opposite of
AutoMapper: everything is explicit, every mapper is a named C# property, and unmapped
destination properties are build-time errors.

## When this applies

Use this skill when the target project references `ArchPillar.Extensions.Mapper` and you are:
mapping an entity to a DTO, writing a LINQ/EF Core projection, defining or editing a
`MapperContext`, mapping enums, or cloning/updating objects. Requires .NET 9+ / C# 13.

## The mental model (read this first)

- Mappings live on a class that inherits **`MapperContext`**. Each mapper is a **public
  property** of type `Mapper<TSource, TDest>` (or `EnumMapper<,>` / `SymmetricEnumMapper<,>`),
  initialized in the constructor. Because mappers are named properties, *Go to Definition* and
  *Find All References* work — that traceability is the entire point of the library.
- A mapper is defined by a **member-init expression**, by **fluent per-property `.Map()`
  calls**, or a **combination** of the two (see below). The library compiles that definition
  into both a compiled delegate (`Map`, for in-memory mapping) and an expression tree
  (`Project` / `ToExpression`, for a LINQ provider).
- The context is **thread-safe**; register it as a **singleton**.

## Two ways to define a mapper

Both styles are first-class and combine freely on the same mapper:

- **Member-init** — pass `src => new TDest { … }` to `CreateMapper`. Preferred for
  straightforward mappings; reads like a normal object initializer, and every assigned property
  is tracked as a required mapping.
- **Fluent per-property** — chain `.Map(dest => …, src => …)`. This is the form used for **mapper
  inheritance** (`Inherit(baseMapper).For<TDerived>().Map(…)`, to add the derived type's extra
  properties) and for **customizing a clone mapper** (see below), and the natural choice for
  `.Optional()` / `.Ignore()` or a computed property.

A **clone mapper** is the one case that needs no explicit mapping: `CreateCloneMapper<T>()`
auto-wires every public settable property as an identity mapping (`dest.Prop = src.Prop`). You
only chain `.Ignore(d => d.X)` to exclude a property or `.Map(d => d.X, s => …)` to override one
— customization is optional.
- **Combined** — start with a member-init for the simple properties, then chain `.Map()`,
  `.Optional()`, or `.Ignore()` for the rest.

## Rules that are easy to get wrong — follow these exactly

These are the constraints an AI tends to violate by defaulting to AutoMapper habits. Honor them
or the code will fail at build time or query time.

1. **No convention-based / name-based auto-mapping. No attributes. No profiles.** Every
   destination property is mapped explicitly, marked `.Optional()`, or `.Ignore()`d. There is
   no `[Map]` attribute and no "map by matching names" — do not invent one.
2. **Coverage is enforced at build time.** Every non-nullable destination property must be
   covered, or `Build()` throws `InvalidOperationException` listing the gaps. Nullable
   destination properties (`int?`, `string?`) are auto-ignored under the default mode.
3. **Object-initializer syntax only — never a parameterized constructor.** Destination types
   need a public parameterless constructor; `CreateMapper` validates this at build time
   regardless of how the mapper is used. Write `src => new TDest { Id = src.Id }`, never
   `src => new TDest(src.Id)`. (If a use case never needs expression projection, a plain
   constructor call is simpler than a mapper.)
4. **EF Core-translatability is required only on the projection path.** A mapper *always*
   compiles to an in-memory delegate; for **in-memory-only** mapping (`Map` / `MapTo`) the
   expression can contain `throw`, delegate/`Func<>` calls, and arbitrary C# methods — they just
   run as compiled code. The translatability rule (no `throw`, no delegate invocation, no
   provider-untranslatable method calls) kicks in *only* when the mapper is consumed via
   `Project()` / `ToExpression()` against a LINQ provider like EF Core. The library's design
   goal is "one definition, two modes," so **prefer** translatable expressions unless you know a
   mapper is in-memory only. When a *nested* mapper can't be translated but its parent is
   projected, opt that nested mapper out of inlining with `Invoke(src.X)` instead of
   `Map(src.X)` (see `references/patterns.md`). (The `_ => throw …` arm in a `CreateEnumMapper`
   switch is always fine — that switch runs at build time to build a lookup table and is never
   embedded in the translated expression.)
5. **Nested mappers are composed by reference, then inlined.** Use the leaf mapper property:
   `Child.Map(src.Child)` for one object, `src.Children.Project(Child).ToList()` for a
   collection. The build step inlines the child's expression into the parent, so the provider
   sees one flat tree — no N+1, no client evaluation.

## Canonical example

```csharp
using ArchPillar.Extensions.Mapper;

public class AppMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }  // declare leaves first
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.Product.Name,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id      = src.Id,
            Status  = src.Status.ToString(),                 // simple, translatable
            IsOwner = src.OwnerId == CurrentUserId,          // runtime variable
            Lines   = src.Lines.Project(OrderLine).ToList(), // nested collection
        })
        .Optional(dest => dest.CustomerName, src => src.Customer.Name); // opt-in join

        EagerBuildAll(); // always last — compiles everything, surfaces errors at startup
    }
}
```

```csharp
// In-memory: every property (required + optional) is always materialized.
OrderDto dto = mappers.Order.Map(order, o => o.Set(mappers.CurrentUserId, userId));

// EF Core projection: required properties only; opt into the rest with Include().
var page = await db.Orders
    .Where(o => o.IsActive)
    .Project(mappers.Order, o => o
        .Include(m => m.CustomerName)
        .Set(mappers.CurrentUserId, currentUser.Id))
    .ToListAsync();
```

## Feature cheat-sheet

| Need | API | Notes |
| --- | --- | --- |
| Map one nested object | `Child.Map(src.Child)` | Auto null-guard; inlined into the parent |
| Nested object, not translatable | `Child.Invoke(src.Child)` | Opt out of inlining; runs in memory on the projection path |
| Map a nested collection | `src.Items.Project(Child).ToList()` | Also `.ToArray()` / `.ToHashSet()` |
| Map a dictionary | `src.Items.ToDictionary(i => i.Key, i => Item.Map(i))` | Inline `Map()` in the value selector |
| Opt-in property | `.Optional(d => d.X, s => …)` then `.Include(m => m.X)` / `.Include("X")` | String paths validated at call time |
| Request-scoped value | `Variable<T>` + `.Set(ctx.Var, value)` | Unbound → `default(T)` unless `CreateVariable<T>(defaultValue:)` |
| Enum → enum | `CreateEnumMapper<TSrc,TDest>(s => s switch { … })` | Generates a translatable conditional; nullable overloads exist |
| Bidirectional enum | `CreateSymmetricEnumMapper<,>` → `Map()` / `MapReverse()` | Validates bijection at build time |
| Shallow copy | `CreateCloneMapper<T>()` | Auto-wires identity mappings; `.Ignore()` / `.Map()` to adjust |
| Update existing object | `mapper.MapTo(src, dest)` | In-memory only; collection modes below |
| Destination hierarchy | `Inherit(Base).For<TDerived>()` | Reuses base mappings; only add the new props |
| Skip a property | `.Ignore(d => d.X)` | Prefer over `CoverageValidation.None` |
| Fix untranslatable pattern | `IExpressionTransformer` / `WithTransformers(…)` | Use sparingly, narrowest scope |

`MapTo` collection modes: `Shallow` (default, replace reference), `Deep` (keep the collection
instance, clear + re-add), `DeepWithIdentity` (match by key → per-element `Modified`/`Added`/
`Deleted`; use this for EF Core change-tracked collections).

Coverage modes: `NonNullableProperties` (default), `AllProperties`, `None`. Set per-mapper with
`.SetCoverageValidation(…)` or per-context by overriding `DefaultCoverageValidation`.

## Production defaults

- Register the context as a **singleton** (`AddSingleton<AppMappers>()`) — transient recompiles
  on every resolve.
- Call **`EagerBuildAll()`** at the end of the constructor so configuration errors surface at
  startup, not on the first request.
- Mark expensive navigation joins as **`.Optional()`** so list endpoints skip them and detail
  endpoints opt in with `.Include()`.

## Validate mappers by building them

Many mapper defects are invisible to the C# compiler — an unmapped non-nullable destination
property, a circular mapper reference, a non-bijective `SymmetricEnumMapper` — and surface only
when the mapper is **built**. Because building is lazy by default, exercise it explicitly:

- **In a unit test**, construct the context with `EagerBuildAll()` in its constructor (or call a
  build helper) and assert it does not throw. Constructing the context *is* the test — it forces
  every mapper to assemble and compile, turning latent config errors into a failed test.
- **At startup**, the same `EagerBuildAll()` call (or a dedicated "resolve and build all mappers"
  health-check mode) surfaces those errors on boot instead of on the first request.

```csharp
[Fact]
public void AppMappers_AllMappersBuild()
{
    // Throws if any mapper has an unmapped property, circular reference,
    // or (for SymmetricEnumMapper) a non-bijective mapping.
    _ = new AppMappers(); // ctor calls EagerBuildAll()
}
```

When a mapper is used with EF Core, also add a test that runs `Project(mapper)` against a **real
relational provider** (SQLite, or Testcontainers/real Postgres/SQL Server) and materializes the
results — that is the only way to prove the expression translated to SQL. The
`EntityFrameworkCore.InMemory` provider does *not* validate translation. These are all
application-code tests; see `references/patterns.md` for the full three-tier approach (build →
in-memory output → SQL translation).

## EF Core companion package

The core library is provider-agnostic — `Project(mapper)` works against any LINQ provider with
nothing extra. The optional `ArchPillar.Extensions.Mapper.EntityFrameworkCore` package adds
direct mapper calls inside hand-written `Select`s and flat SQL `CASE` for enums. **If the task
involves `UseArchPillarMapper`, `ApplyMappers`, calling `Map()`/`Project()` inside your own
`Select`, or enum-to-SQL translation, read [`references/efcore.md`](references/efcore.md).**

## Deeper guidance

- [`references/patterns.md`](references/patterns.md) — pitfalls, when to use which mapping
  style, transformers, circular-reference handling, and how to test mappers (both the
  in-memory and the expression path).
- Full docs live in the repo under `docs/mapper/` (`features.md`, `recommendations.md`,
  `internals/SPEC.md`) and are published via Context7 (`archpillar/extensions`). Prefer those
  for exhaustive API surface; this skill is the working subset.
