# Implementation Plan

## Dependency order

Classes must be implemented in this order — each depends on the ones above it.

---

## Tasks

### 1. `Variable<T>` — add optional name

**File:** `src/Mapper/Variable.cs`

The implicit operator stub already exists and throws correctly (prevents direct invocation).

Identity is **reference equality** — no ID needed. When a `Variable<T>` is captured in a lambda, the expression tree holds a reference to the actual object. The visitor extracts it and compares by reference, which naturally distinguishes variables from different context instances.

An optional `Name` property is useful for error messages.

- [x] Add `public string? Name { get; }` set via constructor
- [x] Add `public T? DefaultValue { get; }` — defaults to `default(T)`, settable at creation time; used by `VariableReplacer` when the variable is not set at the call site
- [x] `CreateVariable<T>(string? name = null, T? defaultValue = default)` passes both through
- [x] The implicit operator body stays as-is (throws — it is only ever called at expression-build time, never at runtime)
- [x] Variable bindings in options use `Dictionary<object, object?>` keyed by the `Variable<T>` instance itself (default reference equality, no `Equals`/`GetHashCode` override needed)

---

### 2. `EnumMapper<TSource, TDest>` — standalone, no dependencies

**File:** `src/Mapper/EnumMapper.cs`

- [x] Store the `Func<TSource, TDest>` mapping method (add internal constructor)
- [x] `Map(source)` → call the stored method directly
- [x] `ToExpression()` → enumerate `Enum.GetValues<TSource>()`, call the method for each value, build a conditional chain:
  `source => source == V1 ? D1 : source == V2 ? D2 : ... : throw`
  Use `Expression.Condition` chained, with the final else being `Expression.Throw` wrapping `ArgumentOutOfRangeException`
- [x] Cache the compiled expression (lazy, thread-safe via `Lazy<>`)

---

### 3. Internal expression visitors — one file per visitor

**New files:** `src/Mapper/Internal/`

These are `ExpressionVisitor` subclasses used during expression building. No public API.

#### 3a. `ParameterReplacer`

**File:** `src/Mapper/Internal/ParameterReplacer.cs`

- [x] Replaces a specific `ParameterExpression` with another expression
- [x] Used when inlining a nested mapper: the nested mapper's source parameter is replaced with the actual access expression from the parent (e.g. `source.Customer`)

#### 3b. `NestedMapperInliner`

**File:** `src/Mapper/Internal/NestedMapperInliner.cs`

- [x] Detects `MethodCallExpression` for `Mapper<X,Y>.Map(expression)` (the expression-safe single-argument overload) and replaces it with the nested mapper's `MemberInitExpression` with the source parameter substituted
- [x] Detects `MethodCallExpression` for `MapperExtensions.Project(source, mapper)` and replaces it with `source.Select(nestedExpression)` with the source parameter substituted
- [x] Detects `MethodCallExpression` for `EnumMapper<X,Y>.Map(expression)` and replaces it with the enum mapper's conditional expression tree with the source parameter substituted
- [x] Extracts mapper instances from the expression tree by reading the target object of the call as a `ConstantExpression` — no constructor arguments needed

#### 3c. `VariableReplacer`

**File:** `src/Mapper/Internal/VariableReplacer.cs`

- [x] Detects `UnaryExpression` (Convert) nodes produced by `Variable<T>`'s implicit operator, where the operand is a `MemberExpression` or `ConstantExpression` holding a `Variable<T>` instance
- [x] Replaces with `Expression.Constant(value, typeof(T))` when the variable has a binding in the provided dictionary, or `Expression.Constant(variable.DefaultValue, typeof(T))` when it does not
- [x] Receives a `Dictionary<object, object?>` keyed by the `Variable<T>` instance (reference equality)

---

### 4. `MapperBuilder<TSource, TDest>` — concrete builder

**File:** `src/Mapper/MapperBuilder.cs`

No separate subclass — the abstract base class is replaced with a single concrete sealed class.

State to track:

- `Expression<Func<TSource, TDest>>? memberInitExpression` — the expression passed to `CreateMapper(expression)`, if any
- `List<PropertyMapping>` — each entry is `(MemberInfo destination, LambdaExpression source, MappingKind kind)` where kind is one of `Required`, `Optional`, or `Ignored`

- [x] `Map(destination, source)` → record as `Required`, return `this`
- [x] `Optional(destination, source)` → record as `Optional`, return `this`
- [x] `Ignore(destination)` → record as `Ignored` with no source expression, return `this`
- [x] `Build()`:
  1. Collect all settable properties of `TDest` via reflection
  2. Determine coverage: extract bound members from the member-init expression bindings (if provided), plus all `Map`/`Optional`/`Ignore` records
  3. Find any property that appears in none of the above → throw `InvalidOperationException` naming all uncovered properties
  4. Normalize mappings into a flat `List<PropertyMapping>`:
     - If a member-init expression was provided, extract its `MemberBinding`s and convert each to a `Required` `PropertyMapping` (re-expressing each binding as a lambda over the source parameter)
     - Merge with any additional `Map`/`Optional`/`Ignore` calls
  5. Run `NestedMapperInliner` on every source expression in the list (replaces nested mapper and enum mapper calls with their expression trees)
  6. Construct and return `Mapper<TSource, TDest>` with the normalized mapping list

---

### 5. `Mapper<TSource, TDest>` — the main class

**File:** `src/Mapper/Mapper.cs`

**Key architectural point:** no stored base expression. `ToExpression` assembles a fresh `MemberInitExpression` each call by selecting the right mappings and running `VariableReplacer`.

#### 5a. Constructor and state

- [x] Replace the stub constructor to accept and store `IReadOnlyList<PropertyMapping> allMappings`
- [x] Add `Lazy<Func<TSource?, TDest?>> _compiledDefault` field, initialized in the constructor to compile `ToExpression()` on first access

#### 5b. `BuildExpression` private helper

Private method `BuildExpression(HashSet<string> includeNames, IReadOnlyDictionary<object, object?> variableBindings, bool nullSafeOptionals)`:

- [x] Filter `_allMappings` to Required mappings + Optional mappings whose `Destination.Name` is in `includeNames`
- [x] For each selected mapping, apply `VariableReplacer` (pass `variableBindings`) to its `Source` lambda body, keeping the same source parameter
- [x] Build a `MemberInitExpression` using the filtered, variable-substituted bindings
- [x] Return `Expression<Func<TSource, TDest>>` — no top-level null guard; `Map` handles null in direct code before invoking the compiled delegate
- [x] When `nullSafeOptionals = true` (compile/in-memory mode): wrap optional source bodies with `AddNullSafeNavigation` to guard intermediate reference-type member accesses
- [x] When `nullSafeOptionals = false` (IQueryable projection mode): use source expressions as-is for provider translatability

#### 5c. `ToExpression` — scalar optionals and variables only

- [ ] Create a `ProjectionOptions<TDest>`, apply the `options` action (if any)
- [ ] Collect `includeNames`: iterate `options.Includes`, add `MemberName` for every `ScalarInclude`; for `StringPathInclude`, split on `.` — if it is a single segment, treat it as a scalar include (multi-segment handled in 5d)
- [ ] Pass `includeNames` and `options.VariableBindings` to `BuildExpression`, return the result

#### 5d. String path validation and unknown-path error

- [ ] For `StringPathInclude` with a **single segment**: look up the name in the mapper's Optional mappings; throw `InvalidOperationException($"Unknown optional property: '{path}'")` if not found
- [ ] For `StringPathInclude` with **multiple segments**: validate that the first segment names an existing Required or Optional mapping; throw if not found. Full multi-segment support (nested element options) is deferred to 5f.

#### 5e. `Map` methods

- [ ] `Map(source)` — if source is null return null, else return `_compiledDefault.Value(source)`
- [ ] `Map(source, options)` — if source is null return null; if options is null delegate to `Map(source)`; else compile `ToExpression(options)` and invoke (not cached)

#### 5f. Collection element options *(can be implemented after 5e)*

Handles `Include(m => m.Lines, line => line.Include(l => l.SupplierName))` and multi-segment string paths like `"Lines.SupplierName"`.

Requires `PropertyMapping` to carry the nested mapper reference (see Revisit item on typed variants). Design options:

- **Option A** — Extend `PropertyMapping` with an optional `IMapper? NestedMapper` field set by the builder when the source is a `Project(nestedMapper)` call. `ToExpression` detects these and rebuilds the collection source expression using the nested mapper's `ToExpression(elementOptions)` instead of the stored inlined lambda.
- **Option B** — Store the nested mapper + source-access expression as separate fields (the full typed-variant revisit). More explicit, easier to maintain.

- [ ] Choose option (A is smaller, B is the clean revisit path)
- [ ] For `CollectionInclude` entries: invoke `NestedOptionsFactory()` to get the nested `ProjectionOptions<TElement>`, call the nested mapper's `ToExpression(nestedOptions)`, rebuild the collection source expression using `Enumerable.Select` with that expression

---

### 6. `MapOptions<TDest>` and `ProjectionOptions<TDest>`

**Files:** `src/Mapper/MapOptions.cs`, `src/Mapper/ProjectionOptions.cs`

Both carry the same internal state. `ProjectionOptions<TDest>` is a separate public type only for API clarity.

Internal state:

- `List<IncludeEntry> includes` — each entry is one of:
  - A scalar include: the destination `MemberInfo` extracted from the lambda
  - A collection include: the destination `MemberInfo` plus a nested `Action<OptionsForElement>` callback
  - A string path: the raw string, resolved against the mapper's optional list at apply time
- `Dictionary<object, object?> variableBindings` — keyed by `Variable<T>` instance (reference equality), boxed value

- [x] `Include<TValue>(lambda)` → extract `MemberInfo` from the lambda, add scalar include entry, return `this`
- [x] `Include<TElement>(collectionLambda, elementOptions)` → extract `MemberInfo`, add collection include entry, return `this`
- [x] `Include(string path)` → store as a string entry; validate and resolve when the options are applied to a mapper
- [x] `Set<T>(variable, value)` → store `(variable instance → boxed value)`, return `this`
- [x] Implement `MapOptions<TDest>` with the same structure

String path resolution (applied in `Mapper<TSource, TDest>.ToExpression`):

- Split path on `.`
- First segment: look up by name in the mapper's optional property list → throw `InvalidOperationException("Unknown optional path: '{path}'")`  if not found
- If more segments remain: the first segment must correspond to a collection optional whose element type has its own mapper → recurse into that nested mapper's optional list

---

### 7. `MapperContext` — factory methods and eager build

**File:** `src/Mapper/MapperContext.cs`

- [x] Store `MapperContextOptions` in a field
- [x] Constructor `MapperContext(Action<MapperContextOptions>)` → create options instance, apply the delegate, store
- [x] Constructor `MapperContext(MapperContextOptions)` → store directly
- [x] `CreateVariable<T>(string? name = null)` → `return new Variable<T>(name)` (static — no context state needed)
- [x] `CreateMapper<TSource, TDest>(expression)` → `return new MapperBuilderImplementation<TSource, TDest>(expression, options)`
- [x] `CreateEnumMapper<TSource, TDest>(mappingMethod)` → `return new EnumMapper<TSource, TDest>(mappingMethod)`
- [x] Eager build — expose `protected void EagerBuildAll()`:
  - Reflect over all public properties of `this` that are of type `Mapper<,>` (open generic check)
  - Call `ToExpression()` on each to force expression assembly and delegate compilation
  - This method is intended to be called at the end of a subclass constructor when `options.EagerBuild` is true

---

### 8. `MapperExtensions`

**File:** `src/Mapper/MapperQueryableExtensions.cs`

- [ ] `IQueryable<TSource>.Project(mapper, options)`:
  - Call `mapper.ToExpression(options)` to get the expression (already includes null check in the body)
  - Call `.Select(expression)` on the queryable and return the resulting `IQueryable<TDest>`

- [ ] `IEnumerable<TSource>.Project(mapper)` (no options, expression-safe overload):
  - Compile `mapper.ToExpression()` to a `Func<TSource?, TDest?>`
  - Return `source.Select(compiled)`

- [ ] `IEnumerable<TSource>.Project(mapper, options)`:
  - Compile `mapper.ToExpression(options)` to a `Func<TSource?, TDest?>`
  - Return `source.Select(compiled)`

---

## Build order summary

```text
Variable<T>
    ↓
EnumMapper<,>
    ↓
ParameterReplacer
    ↓
NestedMapperInliner  (depends on EnumMapper and Mapper for inline detection)
    ↓
VariableReplacer
    ↓
MapperBuilderImplementation<,>  (uses all three visitors)
    ↓
Mapper<,>  (needs MapOptions / ProjectionOptions for method signatures — stubs already exist)
    ↓
MapOptions<TDest> / ProjectionOptions<TDest>
    ↓
MapperContext
    ↓
MapperExtensions
```

---

## Test coverage checklist (verify after each step)

- [ ] `EnumMappingTests` — after EnumMapper
- [ ] `BuilderValidationTests` — after MapperBuilderImplementation
- [ ] `BasicMappingTests` — after Mapper and MapperExtensions
- [ ] `NestedMapperTests` — after NestedMapperInliner
- [ ] `OptionalPropertyTests` — after MapOptions and Mapper
- [ ] `VariableTests` — after VariableReplacer
- [ ] `ProjectionTests` — after MapperExtensions
- [ ] `EfCoreIntegrationTests` — final integration check

---

## Revisit

Concerns gathered during implementation to re-examine after the first working version.

- [ ] **`EnumMapper` throw expression** — The final `else` branch of the conditional chain is `Expression.Throw(ArgumentOutOfRangeException)`. This branch is unreachable for valid enum values, but some query providers (EF Core SQL translation in particular) may refuse to translate a `throw` node even if it is dead code. Verify once `EfCoreIntegrationTests` runs. If translation fails, replace the throw with `Expression.Default(typeof(TDest))` or a sentinel constant as the fallback.
- [ ] **Expression visitor coverage** — `NestedMapperInliner` only handles two source patterns for instance extraction: direct `ConstantExpression` and a single-level `MemberExpression` over a `ConstantExpression`. Deeply nested closures or chain member accesses are not handled. Verify this is sufficient for all real usage patterns once integration tests pass. Similarly, `VariableReplacer` uses the same two-pattern extraction; verify both visitors behave correctly when the same expression tree contains both mapper calls and variable conversions.
- [ ] **`PropertyMapping` — typed variants** — Currently `PropertyMapping` is a flat record with an opaque `LambdaExpression? Source`. After the first working version, consider replacing it with a discriminated union of typed cases: a plain value mapping (just a lambda), a nested object mapping (mapper instance + source-access lambda), and a nested collection mapping (mapper instance + source-access lambda). Extracting the mapper and source access upfront — rather than leaving them embedded in the expression tree — would make `Mapper.ToExpression` more explicit, remove the need for `NestedMapperInliner` to run at build time, and make it easier to add features like per-call optional overrides on nested mappers.
- [ ] **`BuildExpression` does not cascade includes into nested mappers** — When a required collection mapping (e.g. `Lines`) uses an already-inlined `Enumerable.Select` expression, `BuildExpression` emits that fixed expression regardless of any `CollectionInclude` or multi-segment string path requested by the caller. Optional properties on the nested element type (e.g. `SupplierName` on `OrderLineDto`) are therefore always absent. Cascading requires detecting which mappings are backed by a nested mapper and rebuilding their source expression using the nested mapper's `BuildExpression` with the element-level includes forwarded. This is the same problem addressed by task 5f and the typed-variant revisit item above.
