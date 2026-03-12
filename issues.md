# Potential Issues

## ~~1. `IEnumerable.Project` (no-options) recompiles the expression on every call~~ ✓ Fixed

`Map(source, options)`, `MapTo(source, dest, options)`, and `IEnumerable.Project` all now delegate to the pre-compiled `_compiled`/`_compiledMapTo` `Lazy<>` delegates. No recompilation occurs on any path.

---

## ~~2. Only one nested mapper call per property mapping~~ ✓ Fixed

Replaced the two-phase `NestedMapperDetector` approach (detect at build time, stitch at expression-build time) with a `NestedMapperInliner` ExpressionVisitor that runs at expression-build time. It visits the full source expression and replaces every `Mapper.Map()` / `.Project()` call in-place with the inlined body. This supports ternaries, arbitrary nesting, and any number of mapper calls per property expression.

Side effects: `PropertyMapping` simplified (3 fields removed), `NestedMapperDetector` deleted, `BuildExpression` unified. The per-property cascaded `IncludeSet` is passed into the inliner as a constructor argument.

---

## ~~3. Nested mapper calls inside `ToDictionary` lambdas are not supported~~ ✓ Fixed

Solved by the `NestedMapperInliner` approach from issue 2 — an in-place visitor that replaces `Map()` calls wherever they appear (including inside `ToDictionary` value-selector lambdas) handles this naturally.

```csharp
// Now works:
Items = src.Items.ToDictionary(i => i.Key, i => ItemMapper.Map(i)),
```

---

## ~~4. SPEC.md — ToDictionary with nested mapper shown as supported but not yet implemented~~ ✓ Fixed

The spec (lines 155–163) shows `ToDictionary(i => i.Id, i => ItemMapper.Map(i))` as a supported pattern. This now works via `NestedMapperInliner` (issue 2/3 fix). Spec and implementation are aligned.
