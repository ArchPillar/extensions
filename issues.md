# Potential Issues

## 1. ~~`ValidateIncludes` does not recurse — deep path typos are silently ignored~~ SOLVED

`ValidateIncludes` is now called inside `BuildExpression`, so nested mappers validate their includes recursively. For inline `MemberInit` expressions (not backed by a nested mapper), `NestedMapperInliner.ValidateInlineIncludes` checks include names against the binding members. Deep path typos like `"Pack.Primary.Typo"` now throw `InvalidOperationException` at every level.

---

## 2. ~~`EagerBuildAll` skips `EnumMapper<,>` properties~~ SOLVED

`EagerBuildAll` now also enumerates `EnumMapper<,>` properties and compiles them eagerly. Errors in enum mapping methods (e.g. non-exhaustive switch expressions) are surfaced at construction time.

---

## 3. ~~Null guard in `NestedMapperInliner` double-evaluates `srcExpr`~~ NOT AN ISSUE

The source expression (`srcExpr`) appears twice in the null-guarded conditional — once in the null check and once in the inlined body. In theory a side-effecting expression would evaluate twice, but in practice `srcExpr` is always a simple member access (e.g. `src.Customer`) written inside a mapper definition. The only "fix" (`Expression.Block` / `Expression.Assign`) would break EF Core translation, which is a primary goal. No action needed.

---

## 4. ~~`BuildMapToAction` — hidden `MemberAssignment` precondition~~ SOLVED

`BuildMapToAction` now explicitly checks each binding and throws `InvalidOperationException` with a clear message if a non-`MemberAssignment` binding is encountered, instead of silently failing via `.Cast<MemberAssignment>()`.

---

## 5. Auto-ignore of nullable reference types can mask forgotten mappings

`MapperBuilder.RequiresCoverage` auto-ignores any nullable reference type property (`ComplexDto?`, `string?`, etc.). A developer who forgets to map a nullable navigation property gets no build-time error. The SPEC documents this as intentional, but it is the sharpest edge in the builder API.

No fix planned — accepted design trade-off. Worth calling out explicitly in SPEC.md.

---

## 6. `SelectorCompiler` nested inside a generic class generates duplicate JIT code

`SelectorCompiler` and its inner `DictParamFinder` are private nested classes inside `Mapper<TSource, TDest>`. Because the enclosing type is generic, each type-pair instantiation produces a separate JIT-compiled type for these classes, despite being structurally identical across all pairs.

Fix: extract both classes to `Internal/SelectorCompiler.cs` as plain `internal sealed class` types.

---

## 7. `TryExtractVariable` and `IsVariableType` duplicated across two files

`VariableReplacer` and `VariableDictReplacer` each contain identical implementations of `TryExtractVariable` and `IsVariableType`. Any change to variable extraction logic must be applied in both places.

Fix: extract to a shared `internal static class VariableHelper` or a common base visitor.

---

## 8. `IMapper.GetExpression` purpose is unclear — called only once with empty bindings

`IMapper.GetExpression(includes, variableBindings)` exists only for `EagerBuildAll`, which calls it with empty bindings solely to force compilation. The method's name implies general-purpose use but its actual role is "force compile with defaults." This asymmetry with `GetRawExpression` is confusing when reading the interface.

Fix: consider replacing with a dedicated `void Compile()` method on `IMapper`, or documenting the intent in the interface XML doc.

---

## 9. `IsProjectCall` check count constraint is unexplained

`NestedMapperInliner.IsProjectCall` checks `Arguments.Count == 2`, which silently excludes the `IEnumerable.Project(mapper, options)` overload (3 arguments). The reason — that the options overload is not intended for use inside expression trees — is not stated in the code.

Fix: add a comment explaining why the 3-argument overload is intentionally excluded.

---

## 10. `EnumMapper` silent fallback overstated as "unreachable"

`EnumMapper.BuildExpression` uses `Expression.Constant(default(TDest))` as the final else branch and comments it as "unreachable." This is only true when the user's mapping method is exhaustive over all source enum values. If a new source value is added later and the method is not updated, the mapper silently returns `default(TDest)`.

Fix: update the comment to note that the "unreachable" guarantee depends on the mapping method being exhaustive, and that adding a new enum value without updating the method is a silent failure.
