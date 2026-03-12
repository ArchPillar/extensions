# Potential Issues

## ~~1. `IEnumerable.Project` (no-options) recompiles the expression on every call~~ ✓ Fixed

`Map(source, options)`, `MapTo(source, dest, options)`, and `IEnumerable.Project` all now delegate to the pre-compiled `_compiled`/`_compiledMapTo` `Lazy<>` delegates. No recompilation occurs on any path.

---

## 2. Only one nested mapper call per property mapping

**Severity**: Limitation — prevents complex mapping expressions

`NestedMapperDetector` finds the **first** `Mapper.Map()` or `.Project()` call per property mapping and stops. Each `PropertyMapping` carries a single `NestedMapperAccessor` + `NestedSourceAccess` pair. This means a property expression cannot contain multiple mapper calls — e.g. a ternary selecting between different mappers or source paths for the same destination type:

```csharp
Summary = src.IsVip
    ? VipMapper.Map(src.VipProfile)
    : BasicMapper.Map(src.BasicProfile)
```

Both branches return the same `SummaryDto`, so this would be valid C# and translatable by EF Core (same column set, `CASE WHEN` around values). But the current design cannot handle it.

**Idea**: Replace the two-phase approach (detect at build time, stitch at expression-build time) with a `NestedMapperInliner` ExpressionVisitor that runs at expression-build time. It would visit the full source expression and replace every `Mapper.Map()` / `.Project()` call in-place with the inlined body. This would also simplify `PropertyMapping` (remove 3 fields), delete `NestedMapperDetector`, and unify the `BuildExpression` code path.

**Caveat**: Include cascading becomes harder when mapper calls are deeply nested in arbitrary expressions. Currently, `BuildExpression` knows which destination property it's processing and looks up `includes.Nested[destName]` directly. An in-place inliner visiting an arbitrary sub-tree wouldn't have that context. The destination property's cascaded `IncludeSet` would need to be passed into the inliner per-property (following the object tree), which is feasible but adds complexity.

---

## 3. Nested mapper calls inside `ToDictionary` lambdas are not supported

**Severity**: Limitation — prevents using nested mappers for dictionary value projections

`NestedMapperDetector` recurses into sub-expressions including the lambdas passed to `ToDictionary`. When it finds a `Mapper.Map()` call inside the value-selector lambda, it treats the entire property mapping as a scalar nested mapper — extracting the `Map()` argument as the source access and discarding the surrounding `ToDictionary` call. At expression-bind time, the inlined scalar result (`TDest`) doesn't match the destination property type (`Dictionary<TKey, TDest>`), causing an `ArgumentException: Argument types do not match` at `Expression.Bind`.

```csharp
// Does NOT work — NestedMapperDetector intercepts the Map() call:
Items = src.Items.ToDictionary(i => i.Key, i => ItemMapper.Map(i)),

// Works — inline the construction instead:
Items = src.Items.ToDictionary(i => i.Key, i => new ItemDto { Name = i.Name }),
```

This is a specific instance of issue 2: the detector assumes each property mapping contains at most one top-level nested mapper call, but `ToDictionary`'s value-selector lambda places the call in a nested context where it should be left alone.

**Resolution**: Would be solved by the `NestedMapperInliner` ExpressionVisitor approach described in issue 2 — an in-place visitor that replaces `Map()` calls wherever they appear (including inside `ToDictionary` lambdas) would handle this naturally.

---

## 4. SPEC.md — ToDictionary with nested mapper shown as supported but not yet implemented

**Severity**: Documentation — pending implementation

The spec (lines 155–163) shows `ToDictionary(i => i.Id, i => ItemMapper.Map(i))` as a supported pattern. This does not work yet due to issue 3 (`NestedMapperDetector` intercepts the `Map()` call inside the lambda). The spec documents the intended behavior; the implementation needs to catch up via the `NestedMapperInliner` approach described in issue 2.
