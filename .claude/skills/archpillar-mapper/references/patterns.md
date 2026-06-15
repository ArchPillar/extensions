# Mapper — patterns, pitfalls, and testing

Practical guidance beyond the core rules in `SKILL.md`.

## Choosing a mapping style

- **Member-init** (`src => new TDest { … }`) is the default — concise, reads like a normal
  object initializer, and every assigned property is tracked as a required mapping.
- **Fluent** (`.Map(d => d.X, s => …)`) is for properties that need `.Optional()` / `.Ignore()`
  or a complex computed expression. The two combine: start with a member-init for the
  straightforward properties, then chain `.Map()` / `.Optional()` / `.Ignore()` for the rest.

## Declaration order

Nested mapper references resolve lazily, so order does not matter at runtime — but declare
**leaf mappers before their parents** for readability (`OrderLine` before `Order` before
`User`).

## Pitfalls

- **Constructor-based mapping is unsupported.** Destination needs a parameterless constructor;
  use object-initializer syntax. This is intentional so every mapper works in both in-memory and
  LINQ modes — a constructor mapper would silently fail at query time.
- **Null behavior for collections.** An *optional* collection from a `null` source maps to
  `null` (a guard is emitted for in-memory mapping). A *required* collection from a `null`
  source throws `ArgumentNullException`. In EF Core projection the guard is omitted — collection
  navigations are never `null` in the database.
- **`MapTo` is in-memory only** — there is no LINQ/EF Core equivalent. `source == null` is a
  no-op; `destination == null` throws. Scalar-only `MapTo` is zero-allocation.
- **Circular mapper references** (e.g. a self-referencing `TreeNode` mapper) are detected at
  build time and throw `InvalidOperationException`. Map recursive structures manually with a
  depth limit.
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

Test **both** paths — the compiled delegate and the expression tree.

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
