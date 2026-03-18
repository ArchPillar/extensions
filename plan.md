# AOT Source Generator Plan for ArchPillar.Extensions.Mapper

## Problem Analysis

### Current AOT/Trimming Blockers

The mapper library has **5 categories** of AOT-incompatible code:

| # | Location | Issue | Impact |
|---|----------|-------|--------|
| 1 | `Mapper.BuildMapExpression().Compile()` | `Expression.Compile()` emits IL at runtime | Fatal — no interpreter on NativeAOT |
| 2 | `Mapper.BuildMapToAction()` → `.Compile()` | Same | Fatal |
| 3 | `NestedMapperInliner.CompileMapperAccessor()` | Compiles lambda to extract mapper ref | Fatal (build-time only, but still emits IL) |
| 4 | `SelectorCompiler.VisitLambda()` → `node.Compile()` | Pre-compiles nested Select lambdas | Fatal |
| 5 | `VariableDictReplacer` | `GetMethod()` + `MakeGenericMethod()` | Trimming-unsafe |
| 6 | `NestedMapperInliner` static init | `typeof(Enumerable).GetMethods().First(...)` | Trimming-unsafe |
| 7 | `MapperContext.EagerBuildAll()` | `GetType().GetProperties()` + `GetValue()` | Trimming-unsafe |
| 8 | `MapperBuilder.Build()` | `typeof(TDest).GetProperties()` + `NullabilityInfoContext` | Trimming-unsafe |
| 9 | `VariableHelper.TryExtractVariable()` | `PropertyInfo.GetValue()` / `FieldInfo.GetValue()` | Trimming-unsafe |
| 10 | `EnumMapper.BuildExpression()` | `Enum.GetValues<TSource>()` | Trimming-unsafe |

Items 1-4 are **fatal** for NativeAOT — `Expression.Compile()` requires the JIT/Reflection.Emit runtime, which doesn't exist in AOT.

Items 5-10 are trimming hazards — the linker may remove the reflected members.

### Key Insight

The LINQ projection path (`ToExpression()`) is already AOT-safe — it returns expression trees for EF Core, never compiles them. Only the **in-memory mapping path** (`Map()`, `MapTo()`) depends on `Expression.Compile()`.

A source generator can emit **direct C# mapping methods** at compile time, replacing the expression-compiled delegates for in-memory use. The expression tree path stays unchanged for LINQ providers.

## Design: Source Generator Strategy

### Trigger: `partial` MapperContext subclass

The source generator activates **only** when a `MapperContext` subclass is declared `partial`. Non-partial subclasses continue using the current expression-compilation path unchanged — zero breaking changes.

```csharp
// Opt-in: source generator emits direct mapping methods
public partial class AppMappers : MapperContext { ... }

// Opt-out: unchanged runtime behavior
public class AppMappers : MapperContext { ... }
```

### Mapper Identity: Property Name as Key

Mappers have no built-in unique ID — two mappers can share the same `TSource`/`TDest` type pair with completely different mappings (e.g., `Mapper<Order, OrderDto> OrderSummary` and `Mapper<Order, OrderDto> OrderDetail`). The **property name on the MapperContext subclass** is the only stable, unique identifier.

The generator uses property names to:
- **Name generated methods**: `GeneratedMap_{PropertyName}()`, `GeneratedMapTo_{PropertyName}()`
- **Wire delegates**: each property's `SetCompiledDelegates()` call references its own generated method
- **Resolve nested mappers**: `Product.Map(src.Product)` in user code → `GeneratedMap_Product(src.Product, vars)` in generated code, matched by the property name `Product`

This mirrors how users already reference mappers at runtime (`mapper.Order.Map(order)`) and is unambiguous within a single MapperContext subclass.

### What the Generator Emits

For each `partial` MapperContext subclass, the generator analyzes the constructor to find `CreateMapper<TSource, TDest>(...)` calls with their member-init expressions, `.Map()`, `.Optional()`, `.Ignore()` chains, and `CreateEnumMapper<TSource, TDest>(...)` calls. It then emits:

#### 1. Static mapping methods (replacing `Expression.Compile()`)

For a mapper like:
```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id       = src.Id,
    PlacedAt = src.CreatedAt,
    Status   = OrderStatusMapper.Map(src.Status),
    IsOwner  = src.OwnerId == CurrentUserId,
    Lines    = src.Lines.Project(OrderLine).ToList(),
})
.Optional(dest => dest.CustomerName, src => src.Customer.Name);
```

The generator emits:
```csharp
// Auto-generated
public partial class AppMappers
{
    private static OrderDto GeneratedMap_Order(
        Order src,
        List<(object, object?)>? vars)
    {
        return new OrderDto
        {
            Id       = src.Id,
            PlacedAt = src.CreatedAt,
            Status   = GeneratedMap_OrderStatusMapper(src.Status),
            IsOwner  = src.OwnerId == VariableDictReplacer.GetVariable<int>(vars, ???, 0),
            Lines    = src.Lines.Select(item => GeneratedMap_OrderLine(item, vars)).ToList(),
            CustomerName = src.Customer.Name,  // all optionals included in compiled path
        };
    }

    private static void GeneratedMapTo_Order(
        Order src,
        OrderDto dest,
        List<(object, object?)>? vars)
    {
        dest.Id       = src.Id;
        dest.PlacedAt = src.CreatedAt;
        // ... property assignments ...
    }
}
```

#### 2. Enum mapping methods (direct switch, no expression building)

```csharp
// Already exists as user code — generator just references the user's method
// OR generates an equivalent switch if the method is private/inlined
```

For `EnumMapper`, the user's `Func<TSource, TDest>` is already a direct method call — no `Expression.Compile()` involved. `EnumMapper.Map()` just calls `mappingMethod(source)`. The AOT issue is only in `BuildExpression()` for the LINQ path, which is already fine.

#### 3. Registration bridge — connecting generated methods to `Mapper<,>` instances

The `Mapper<TSource, TDest>` class needs a way to use the generated static method instead of `_compiled.Value`. Two approaches:

**Option A — Constructor injection of pre-compiled delegates:**

```csharp
// In Mapper.cs, add an internal constructor overload:
internal Mapper(
    IReadOnlyList<PropertyMapping> allMappings,
    Func<TSource?, List<(object, object?)>?, TDest?> compiledMap,
    Action<TSource, TDest, List<(object, object?)>?> compiledMapTo)
{
    _allMappings   = allMappings;
    _compiled      = new Lazy<...>(() => compiledMap);
    _compiledMapTo = new Lazy<...>(() => compiledMapTo);
}
```

The generated code in the partial class wires this up:

```csharp
public partial class AppMappers
{
    // Generated: called from constructor after base CreateMapper
    private void WireAotDelegates()
    {
        Order.SetCompiledDelegates(
            (src, vars) => src is null ? default : GeneratedMap_Order(src, vars),
            (src, dest, vars) => GeneratedMapTo_Order(src, dest, vars));
    }
}
```

**Option B (Preferred) — Internal `SetCompiledDelegates` method on `Mapper<,>`:**

Add an internal method that replaces the lazy delegates. The generator emits a `partial void OnConstructed()` hook or a static initializer that calls it. This avoids changing the constructor signature.

```csharp
// Added to Mapper.cs
internal void SetCompiledDelegates(
    Func<TSource?, List<(object, object?)>?, TDest?> mapFunc,
    Action<TSource, TDest, List<(object, object?)>?> mapToAction)
{
    _compiled      = new Lazy<...>(() => mapFunc);
    _compiledMapTo = new Lazy<...>(() => mapToAction);
}
```

### Variable Handling in Generated Code

Variables are the trickiest part. In the expression path, `Variable<T>` nodes appear as `Convert(Variable<T>)` and get replaced by `VariableDictReplacer` with calls to `GetVariable<T>(bindings, key, default)`.

In the generated code, the generator knows which variables are referenced (they appear as property accesses on `this` — e.g., `CurrentUserId`). The generated method calls `VariableDictReplacer.GetVariable<T>()` directly with the variable instance as the key:

```csharp
private OrderDto GeneratedMap_Order(Order src, List<(object, object?)>? vars)
{
    return new OrderDto
    {
        IsOwner = src.OwnerId == VariableDictReplacer.GetVariable<int>(vars, CurrentUserId, CurrentUserId.DefaultValue),
    };
}
```

Wait — this requires the generated method to access `this.CurrentUserId` (the `Variable<int>` instance). The generated method must be an **instance method**, not static, since it references context properties (variables and potentially other mappers for nested inlining).

Revised signature:
```csharp
private OrderDto GeneratedMap_Order(Order src, List<(object, object?)>? vars)
```

### Nested Mapper Inlining in Generated Code

For nested mapper calls like `Product.Map(src.Product)`, the generator has two strategies:

1. **Inline the nested mapper's body** (like `NestedMapperInliner` does for expressions) — generates a direct call to the nested mapper's generated method.
2. **Delegate to the nested mapper's generated method** — simpler, same performance.

Strategy 2 is preferred:
```csharp
private OrderLineDto GeneratedMap_OrderLine(OrderLine src, List<(object, object?)>? vars)
{
    return new OrderLineDto
    {
        Product = src.Product is null ? null : GeneratedMap_Product(src.Product, vars),
        // null-guard for reference types, same as NestedMapperInliner
    };
}
```

For `Project()` calls:
```csharp
Lines = src.Lines.Select(item => GeneratedMap_OrderLine(item, vars)).ToList(),
```

### Cross-Context Nested Mappers

When a mapper references a mapper from a different `MapperContext` (e.g., `BookMappers` referencing `publisherMappers.Publisher.Map(...)`), the generator **cannot** inline across contexts. For these cases:

- The generated code falls back to calling `mapper.Map(source)` on the external mapper instance
- The external mapper may or may not be AOT-optimized (depends on whether it's also `partial`)
- This is safe because `Mapper.Map()` always works — it just uses expression compilation if no AOT delegate is set

### EagerBuildAll Replacement

The generator emits a `WireAotDelegates()` call that replaces the need for reflection-based `EagerBuildAll()`. The generated partial class hooks into construction:

```csharp
public partial class AppMappers
{
    // Generator emits this as a partial method or a static constructor hook
    partial void OnAotInitialize()
    {
        Order.SetCompiledDelegates(
            (src, vars) => src is null ? default : GeneratedMap_Order(src, vars),
            (src, dest, vars) => GeneratedMapTo_Order(src, dest, vars));
        // ... for each mapper property ...
    }
}
```

The base `MapperContext` gets a `partial void OnAotInitialize()` that subclasses can implement.

## Project Structure

```
src/
├── Mapper/                                    # Existing library (modified minimally)
│   ├── ArchPillar.Extensions.Mapper.csproj
│   ├── Mapper.cs                              # + SetCompiledDelegates() method
│   ├── MapperContext.cs                       # + partial void OnAotInitialize()
│   └── ...
│
├── Mapper.Generators/                         # NEW: Source generator project
│   ├── ArchPillar.Extensions.Mapper.Generators.csproj
│   ├── MapperContextGenerator.cs              # IIncrementalGenerator entry point
│   ├── Analysis/
│   │   ├── MapperContextAnalyzer.cs           # Finds partial MapperContext subclasses
│   │   ├── CreateMapperCallAnalyzer.cs        # Parses CreateMapper<,>() + fluent chain
│   │   ├── MemberInitAnalyzer.cs              # Extracts property bindings from lambda
│   │   ├── NestedMapperDetector.cs            # Detects .Map() and .Project() calls
│   │   └── VariableDetector.cs                # Detects Variable<T> references
│   ├── Models/
│   │   ├── MapperContextInfo.cs               # Semantic model of a MapperContext subclass
│   │   ├── MapperInfo.cs                      # One Mapper<S,D> property + its config
│   │   ├── PropertyMappingInfo.cs             # Destination property + source expression
│   │   ├── EnumMapperInfo.cs                  # One EnumMapper<S,D> property
│   │   └── VariableInfo.cs                    # Variable<T> property reference
│   ├── Emitters/
│   │   ├── MapMethodEmitter.cs                # Generates Map method body
│   │   ├── MapToMethodEmitter.cs              # Generates MapTo method body
│   │   ├── WiringEmitter.cs                   # Generates SetCompiledDelegates calls
│   │   └── SourceBuilder.cs                   # IndentedTextWriter helper
│   └── Diagnostics/
│       └── DiagnosticDescriptors.cs           # Compiler warnings/errors
│
tests/
├── Mapper.Generators.Tests/                   # NEW: Generator unit tests
│   ├── ArchPillar.Extensions.Mapper.Generators.Tests.csproj
│   ├── GeneratorSnapshotTests.cs              # Verify generated output
│   └── ...
```

## Implementation Steps

### Phase 1: Core Library Changes (minimal, non-breaking)

1. **Add `SetCompiledDelegates()` to `Mapper<TSource, TDest>`**
   - Internal method that replaces the `Lazy<>` fields
   - Fields `_compiled` and `_compiledMapTo` change from `readonly` to mutable (or use a wrapper)

2. **Add AOT initialization hook to `MapperContext`**
   - `partial void OnAotInitialize()` — no-op unless generated
   - Called at end of base construction path

3. **Make `VariableDictReplacer.GetVariable<T>()` public** (already public, good)

4. **Add `[InternalsVisibleTo]` for the generator project** (generators run at compile time, may not need this — they emit into the user's assembly)

### Phase 2: Source Generator — Analysis Pipeline

5. **`IIncrementalGenerator` entry point** — register syntax/semantic providers
6. **Find `partial class X : MapperContext`** candidates via syntax filter
7. **Parse constructor body** — walk the semantic model to extract:
   - `CreateMapper<S,D>(lambda)` calls with member-init expressions
   - `.Map()`, `.Optional()`, `.Ignore()` fluent chain calls
   - `CreateEnumMapper<S,D>(method)` calls
   - `CreateVariable<T>()` property assignments
8. **Build `MapperContextInfo` model** — intermediate representation of all mappers, variables, and their relationships

### Phase 3: Source Generator — Code Emission

9. **Emit `GeneratedMap_XYZ()` instance methods** for each mapper
   - Translate member-init bindings to direct property assignments in `new TDest { ... }`
   - Replace nested `Mapper.Map()` with calls to `GeneratedMap_Nested()`
   - Replace `collection.Project(mapper)` with `.Select(x => GeneratedMap_Nested(x, vars))`
   - Replace `Variable<T>` references with `VariableDictReplacer.GetVariable<T>(vars, variable, default)`
   - Add null-guards for reference-type nested mapper sources
   - Include ALL properties (required + optional) in the generated Map — the compiled delegate always includes everything

10. **Emit `GeneratedMapTo_XYZ()` instance methods** — same as Map but assigns to existing `dest`

11. **Emit wiring method** — `partial void OnAotInitialize()` that calls `SetCompiledDelegates()` on each mapper property

### Phase 4: Diagnostics

12. **Warning when `partial` is missing but AOT is targeted** — suggest adding `partial`
13. **Error for unsupported patterns** — cross-context mappers, complex expressions the generator can't parse
14. **Info diagnostic** — "AOT mapping generated for AppMappers.Order (5 properties)"

### Phase 5: Testing

15. **Snapshot tests** — verify generated code output for canonical mapper patterns
16. **Integration tests** — ensure generated mappers produce identical results to expression-compiled ones
17. **AOT validation** — `dotnet publish -r linux-x64 /p:PublishAot=true` smoke test

## Scope Boundaries

### In Scope
- `Mapper<TSource, TDest>` in-memory `Map()` and `MapTo()` — replace `Expression.Compile()`
- Nested mapper calls within the same context — direct method calls
- `Variable<T>` resolution — compile-time wiring to `GetVariable<T>()`
- `EnumMapper<,>` — already AOT-safe for `Map()`, no changes needed
- Optional properties — all included in generated compiled path (matches current behavior)

### Out of Scope (unchanged)
- `ToExpression()` / LINQ projection path — already expression-tree based, no compilation
- `MapperBuilder` validation — reflection at build time is fine (runs at app startup, not AOT-critical)
- `EagerBuildAll()` — can remain for non-AOT scenarios
- Cross-context nested mappers — fall back to the mapper's own `Map()` method
- `NestedMapperInliner`, `ParameterReplacer`, `VariableReplacer`, `SelectorCompiler` — unchanged, used only by expression path

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| Generator can't parse all expression patterns | Fall back to runtime compilation; emit diagnostic |
| Variable identity (`ReferenceEquals`) changes with generated code | Generated code references the same `Variable<T>` property instance |
| Behavioral parity between generated and expression-compiled paths | Integration tests comparing both outputs |
| Generator increases compile time | Incremental generator with proper caching |
| Cross-context mappers | Explicitly out of scope; fall back to existing path |
