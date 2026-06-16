# Mapper — patterns, pitfalls, and testing

Practical guidance beyond the core rules in `SKILL.md`.

## Choosing a mapping style

- **Member-init** (`src => new TDest { … }`) is the default — concise, reads like a normal
  object initializer, and every assigned property is tracked as a required mapping.
- **Fluent** (`.Map(d => d.X, s => …)`) is a co-equal style, not just a fallback. It is the
  **required** form for **mapper inheritance** (`Inherit(baseMapper).For<TDerived>()`) and
  **clone-mapper customization** (`CreateCloneMapper<T>().Map(…)` / `.Ignore(…)`), and the
  natural choice for `.Optional()` / `.Ignore()` or a computed expression. The two styles
  combine: start with a member-init for the straightforward properties, then chain `.Map()` /
  `.Optional()` / `.Ignore()` for the rest.

## Projection vs in-memory: when translatability matters

A mapper compiles to **both** a delegate (in-memory `Map` / `MapTo`) and an expression tree
(`Project` / `ToExpression`). EF Core-translatability constraints — no `throw`, no delegate
invocation, no provider-untranslatable method calls — apply **only when you actually use the
projection path**. A mapper used purely in memory may contain arbitrary C#. The parameterless
constructor / object-initializer requirement, by contrast, is **always** enforced (validated by
`CreateMapper` at build time), as is coverage validation.

### `Invoke` — opting a nested mapper out of inlining

When a *nested* mapper cannot or should not be translated (it routes through a custom method, or
relies on logic the LINQ provider rejects) but its parent is still projected, call
`Invoke(src.X)` instead of `Map(src.X)`:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Form = FormMapper.Invoke(src.Form),   // NOT inlined; runs on the materialised source
});
```

`Map(src.X)` is folded into the query and never actually called; `Invoke(src.X)` is rewritten
into a call to the nested mapper's already-compiled delegate (carried in a `MapperInvokeBox`
that EF lifts to a query parameter), so the provider materializes `src.Form` and runs the nested
mapper in memory. `Invoke` behaves identically to `Map` for in-memory mapping, works on the
`Project` / `ToExpression` path with or without the EF Core integration, and applies to single
objects (for collections, the inlined `Project` path remains the translatable option).

> Limitation: a *direct* `mapper.Map(o)` typed into a hand-written query whose mapper itself
> contains an `Invoke` is not supported by the automatic interceptor — use the `Project` /
> `ToExpression` path (or `ApplyMappers()`) for those. See `efcore.md`.

## Declaration order

Nested mapper references resolve lazily, so order does not matter at runtime — but declare
**leaf mappers before their parents** for readability (`OrderLine` before `Order` before
`User`).

## Pitfalls

- **Constructor-based mapping is unsupported.** Destination needs a public parameterless
  constructor; use object-initializer syntax. `CreateMapper` validates this at build time
  regardless of how the mapper is used. If a use case never needs expression projection, a plain
  constructor call is simpler and more explicit than a mapper.
- **Null source → null destination.** Every mapper returns `null` for a `null` source, on both
  paths — no configuration. In-memory mapping does an upfront null check; projection emits no
  guard (the LINQ provider handles null propagation).
- **Null behavior for collections.** An *optional* collection from a `null` source maps to
  `null` (a guard is emitted for in-memory mapping). A *required* collection from a `null`
  source throws `ArgumentNullException`. In EF Core projection the guard is omitted — collection
  navigations are never `null` in the database.
- **`MapTo` is in-memory only** — there is no LINQ/EF Core equivalent. `source == null` is a
  no-op; `destination == null` throws. Scalar-only `MapTo` is zero-allocation.
- **Design models to be acyclic.** Avoid circular references in the source/destination model
  graphs (e.g. `Parent` ↔ `Child` back-references, self-referencing `TreeNode`). There is a
  nesting depth-limit backstop, but working against it is a hassle — prefer breaking the cycle in
  the DTO design instead (omit the back-reference, use a summary DTO without it, or mark one side
  `.Optional()`/`.Ignore()`). Self-referencing mapper chains are caught at build time with a
  clear error rather than a stack overflow.
- **`.Ignore()` over `CoverageValidation.None`.** Ignoring a single property documents intent
  and keeps the safety net active for the rest. `None` lets unmapped non-nullable value types
  silently receive `default`.
- **Variables vs inline constants.** Unset variables resolve to `default(T)`; supply a
  `defaultValue` when that is wrong. In EF Core inline calls, variable values bake in as
  captured constants — prefer `Include()` for optionals.

## Enum mapping

- Use `EnumMapper<,>` for many-to-one; use `SymmetricEnumMapper<,>` for true bijections (it
  validates one-to-one at build time and derives the reverse, so the forward and reverse can't
  drift apart).
- The `_ => throw …` arm in the `switch` is fine — the library invokes the switch per enum value
  at build time to record results; the throw is never translated. Nullable overloads:
  `Map(T)` → `TDest`, `Map(T?)` → `TDest?` (null in → null out), `Map(T?, TDest default)` →
  `TDest` (null in → default out).
- Expression access: `ToExpression()`, `ToNullableExpression()`, and (symmetric)
  `ToReverseExpression()`, `ToReverseNullableExpression()`.

## Expression transformers

Use only when you cannot fix EF Core translation by adjusting the expression directly. Prefer
editing the mapping expression (e.g. `(decimal)s.Amount`) over a transformer. Register at the
narrowest scope: **per-mapper > per-context > global** (they run global → context → mapper).
Built-in base classes: `MethodCallTransformer` (replace a specific method call) and
`CastTransformer<TSource, TTarget>` (replace implicit/explicit conversions). A transformer must
return an expression of the same type and the body must stay a `MemberInitExpression`.

## Composing contexts

Multiple `MapperContext` subclasses compose via plain constructor injection — no library support
needed. Register each context plus the aggregate in DI; organize however suits the project.

## Testing mappers

**First, test that every mapper builds.** Most mapper defects are invisible to the C# compiler —
an unmapped non-nullable destination property, a circular mapper reference, a non-bijective
`SymmetricEnumMapper` — and only surface when the mapper is *built* (which is lazy by default).
The cheapest, highest-value test is to force a full build and assert it does not throw:

```csharp
[Fact]
public void AppMappers_AllMappersBuild()
{
    _ = new AppMappers(); // ctor ends with EagerBuildAll() → assembles + compiles every mapper
}
```

Equivalently, run `EagerBuildAll()` (or a dedicated "resolve and build all mappers" startup mode)
on boot so the same errors fail fast in CI / at deploy time rather than on the first request. If
your context does not call `EagerBuildAll()` in its constructor, call it explicitly in the test.

**Then test the output** — exercise **both** paths, the compiled delegate and the expression tree.

```csharp
[Fact]
public void Order_Map_ProducesCorrectDto()
{
    var mappers = new AppMappers();
    var order = new Order { Id = 1, Status = "Active" };

    OrderDto result = mappers.Order.Map(order);

    Assert.Equal(1, result.Id);
    Assert.Equal("Active", result.Status);
}

[Fact]
public void Order_ToExpression_ProducesValidExpression()
{
    var mappers = new AppMappers();

    var expression = mappers.Order.ToExpression();
    Func<Order, OrderDto> compiled = expression.Compile();

    OrderDto result = compiled(new Order { Id = 1, Status = "Active" });
    Assert.Equal(1, result.Id);
}
```

For projection, exercise it against `Microsoft.EntityFrameworkCore.InMemory` with
`Project(mappers.Order).ToListAsync()` and assert the results materialize.

## Performance notes

- Singleton registration avoids recompilation; `EagerBuildAll()` removes cold-start latency.
- Scalar-only `MapTo` is zero-allocation.
- Collection mappings cost the same as `Select + ToList/ToArray/ToHashSet` — the mapper adds no
  overhead beyond the LINQ operation.
- Each variable adds a small per-call binding allocation; avoid many variables in hot paths.
