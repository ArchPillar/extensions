# Potential Issues

## 1. ~~NestedMapperDetector / NestedMapperInliner rely on mapper instances existing at Build() time~~ SOLVED

`NestedMapperDetector` now compiles a deferred `Func<IMapper>` accessor instead of eagerly extracting the mapper instance. `NestedMapperInliner` was deleted entirely — nested mappers are resolved via `IMapper.GetExpression` at expression-build time, not at builder-build time. Declaration order no longer matters (verified by `ReverseOrderMappers` tests).

---

## 2. ~~Variable bindings are not propagated to nested mappers during cascaded optional includes~~ SOLVED

`IMapper.GetExpression` now accepts `IReadOnlyDictionary<object, object?> variableBindings` and `BuildExpression` forwards the parent's variable bindings to nested mappers. Variables like `CurrentUserId` now resolve correctly at any nesting depth (verified by `Map_VariablePropagatedToNestedMapper_ViaInclude` and `Project_VariablePropagatedToNestedMapper_ViaInclude` tests).

---

## 3. Map/Project with options recompiles the expression on every call — no caching

**Severity**: Performance — potentially significant in hot paths

`Mapper.Map(source, options)` calls `BuildExpression(...).Compile()` on every invocation when `options != null`. Similarly, the `Project(IEnumerable, mapper)` extension calls `mapper.ToExpression().Compile()` every time. Expression compilation is expensive (reflection emit under the hood).

Only the default (no-options) path benefits from the `Lazy<>` cache in `_compiledDefault`.

**Idea**: For the `IEnumerable` extension, cache the compiled delegate in a `Lazy<>` on the mapper (it's the same expression as `_compiledDefault`). For the options path, consider a cache keyed on the set of include names + variable bindings. Even a small LRU or a `ConcurrentDictionary` with a composite key would help the common case where the same include set is used repeatedly.

---

## 4. ~~String path resolution only supports 2-level deep paths~~ SOLVED

`ResolveStringPath` was deleted. String paths are now decomposed into a recursive `IncludeSet` tree by `IncludeSet.AddStringPath`, which naturally supports arbitrary depth. Each segment becomes a name at the corresponding level; intermediate segments create nested `IncludeSet` entries automatically.

---

## 5. ~~EagerBuild option has no effect — `EagerBuildAll()` is never called automatically~~ SOLVED

`MapperContextOptions` was removed. `EagerBuildAll()` remains as a protected method on `MapperContext` that subclasses call explicitly at the end of their constructor when eager compilation is desired (e.g. `public EagerTestMappers() { EagerBuildAll(); }`). No misleading flag, no magic.

---

## 6. ~~Duplicate property mappings are not validated~~ SOLVED

Duplicate destination properties are now deduplicated in `Build()` with last-wins semantics. This allows a fluent `.Map()` call to override a member-init binding for the same property, which is a useful pattern for partial overrides.

---

## 7. ~~Duplicate code between NestedMapperDetector and NestedMapperInliner~~ SOLVED

`NestedMapperInliner` was deleted entirely. Only `NestedMapperDetector` remains — it records metadata at build time, and `BuildExpression` handles inlining via `IMapper.GetExpression`. No duplicate code.

---

## 8. ~~AddNullSafeNavigation only guards simple member-access chains~~ SOLVED

`AddNullSafeNavigation` was removed entirely. Source expression chain nullability is the user's responsibility via compiler nullability annotations. The only null guard the library adds is for optional nested scalar mappers — when the source object itself (e.g. `src.Product`) is null, the expression returns `default` instead of throwing.

---

## 9. ~~MapOptions and ProjectionOptions are nearly identical — code duplication~~ SOLVED

`MapOptions` was stripped down to only variable bindings (`Set<T>`), since in-memory mapping always includes all optional properties with null-safe guards. `ProjectionOptions` retains `Include` + `Set<T>` for controlling which optionals are queried in LINQ projections. The two classes are now distinct in purpose and share no duplicated code.

---

## 10. ~~IQueryable.Project null guard may interfere with real LINQ providers~~ SOLVED

The top-level null guard was removed entirely. `Project` now passes the mapper's expression directly to `query.Select()` without any wrapping. Null handling is the caller's responsibility — consistent with how LINQ providers and EF Core work natively.

---

## 11. Only one nested mapper call per property mapping

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
