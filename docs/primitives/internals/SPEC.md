# ArchPillar.Extensions.Primitives — Specification

## Overview

Primitives is the foundation layer of the `ArchPillar.Extensions.*` family. Its
operation-result types give application code a single, allocation-conscious vocabulary for
returning HTTP-aligned outcomes — a status, an optional RFC 7807 problem body, and an
internal-only exception — without coupling the domain to ASP.NET Core. The types are plain
data with `init`-only properties, constructed exclusively through static factories on
`OperationResult`, and serialize over the wire as `application/problem+json`.

The package's assembly base namespace is `ArchPillar.Extensions`. Topic areas live under
sub-namespaces: the operation-result family ships under `ArchPillar.Extensions.Operations`,
typed identifiers under `ArchPillar.Extensions.Models`.

## Goals

- Provide a small, closed vocabulary for operation outcomes that maps one-to-one to HTTP
  status codes, so a result returned from a handler becomes an HTTP response without a
  translation layer.
- Carry failures as structured, RFC 7807-shaped data (`OperationProblem` / `OperationError`)
  that JSON-serializes directly into a problem-details payload.
- Let a failure be *returned* from any handler signature without the caller repeating the
  payload type, and *thrown* from any code path when an early exit is cleaner.
- Stay dependency-free (BCL only), allocation-conscious (no `Problem` body on success), and
  AOT/trim-safe.
- Make every construction path explicit and discoverable through static factories — no
  ambient state, no registry, no reflection at the call site.

## Non-Goals

- Not a general-purpose `Result<T, TError>` monad. The error channel is fixed to the
  HTTP-aligned `OperationProblem` shape, not an arbitrary error type.
- No `Map` / `Bind` / `Match` combinator pipeline. Branch on `IsSuccess` / `Status`, or
  `Unwrap` at the boundary.
- No dependency on ASP.NET Core, MVC, or `Microsoft.AspNetCore.Http`. The types are
  HTTP-*shaped*, not HTTP-*coupled*.
- No localization of `Title` / `Detail` beyond what the caller supplies.
- The core package does not depend on EF Core. Typed-id persistence is an opt-in
  [integration package](../features.md#ef-core-integration-archpillarextensionsprimitivesentityframeworkcore).

## OperationStatus enum & HTTP alignment

`OperationStatus` is an `int`-backed enum whose members carry the matching HTTP numeric
value, so `(int)OperationStatus.NotFound == 404`. The set is deliberately narrow — only the
codes commonly returned from command handlers — and `None = 0` is the unset default, treated
as a failure.

```csharp
public enum OperationStatus
{
    None = 0,
    Ok = 200,
    Created = 201,
    Accepted = 202,
    NoContent = 204,
    BadRequest = 400,
    Unauthorized = 401,
    Forbidden = 403,
    NotFound = 404,
    Conflict = 409,
    Gone = 410,
    PreconditionFailed = 412,
    UnprocessableEntity = 422,
    TooManyRequests = 429,
    InternalServerError = 500,
    NotImplemented = 501,
    ServiceUnavailable = 503,
}
```

Because the underlying type is `int`, a code outside the named set round-trips through a cast
— `(OperationStatus)418` — so a status arriving from an HTTP boundary is never lost. Success
is defined numerically, not by enumeration: a status counts as success when its integer value
is in `[200, 300)`.

`OperationStatusExtensions` is the single source of truth for the constant strings a status
should carry on the wire:

```csharp
public static string Type(this OperationStatus status);   // "not_found", "conflict", …
public static string Title(this OperationStatus status);  // "Not Found", "Conflict", …
```

The failure factories use these for the default `Problem.Type` and `Problem.Title`. Unknown
statuses fall back to `status.ToString().ToLowerInvariant()` and `status.ToString()`.

## OperationResult / OperationResult&lt;TValue&gt;

`OperationResult` is the base outcome: a `Status` plus an optional `Problem` body and an
internal-only `Exception`. On success `Problem` is `null` — there is no body to allocate.
`OperationResult<TValue>` is the sealed subclass that adds the typed `Value` payload returned
on success.

```csharp
public class OperationResult
{
    public OperationStatus Status { get; init; } = OperationStatus.None;
    public OperationProblem? Problem { get; init; }
    public Exception? Exception { get; init; }      // [JsonIgnore]

    public bool IsSuccess { get; }                  // (int)Status is >= 200 and < 300
    public bool IsFailure { get; }                  // !IsSuccess

    public OperationResult ThrowIfFailed();
    public void Unwrap();
}

public sealed class OperationResult<TValue> : OperationResult
{
    public TValue? Value { get; init; }
    public new TValue Unwrap();
}
```

Both expose only `init`-only properties; once a factory builds a result it is effectively
immutable.

## OperationProblem / OperationError — RFC 7807 shape

`OperationProblem` is the error body, modelled on RFC 7807 `application/problem+json` so its
JSON shape can be returned straight from an HTTP boundary. Top-level failures (auth,
not-found, conflict) describe themselves with `Title` / `Detail` plus optional `Extensions`;
field-bearing failures (validation) aggregate per-field into `Errors`.

```csharp
public sealed class OperationProblem
{
    public string? Type { get; init; }       // short slug: "validation", "forbidden"
    public string? Title { get; init; }       // reason phrase, constant per class
    public string? Detail { get; init; }      // per-occurrence explanation
    public string? Instance { get; init; }    // optional occurrence URI / request id
    public IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? Errors { get; init; }
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}

public sealed record OperationError(
    string Type,
    string Detail,
    OperationStatus Status,
    IReadOnlyDictionary<string, object?>? Extensions = null);
```

`OperationError` is the per-error item inside `Errors`, keyed by field name. Every member is
tagged with the lowercase RFC 7807 JSON name (`type`, `title`, `detail`, `instance`,
`errors`, `extensions`, `status`).

## OperationException

`OperationException` carries an `OperationResult` so a failure can travel through `throw` and
be reconstituted at a catch site (typically a dispatcher's exception middleware).

```csharp
public sealed class OperationException : Exception
{
    public OperationException(OperationResult result);
    public OperationException(OperationStatus status, string detail);
    public OperationResult Result { get; }
}
```

The exception message is derived from the carried result's `Problem.Detail`, then `Title`,
then the status alone. When the result captured an inner `Exception`, it is threaded through
as `InnerException`.

## Implicit conversions

Three conversions remove boilerplate at handler boundaries:

```csharp
// On OperationResult
public static implicit operator Task<OperationResult>(OperationResult result);  // (1)
public static implicit operator Exception(OperationResult result);              // (2)

// On OperationResult<TValue>
public static implicit operator Task<OperationResult<TValue>>(OperationResult<TValue> r);  // (3)
public static implicit operator OperationResult<TValue>(TValue value);                      // (4)
public static implicit operator OperationResult<TValue>(OperationFailure failure);          // (5)
```

- **(1)/(3)** let a synchronous result satisfy a `Task<OperationResult[<T>]>` return type.
- **(2)** turns `throw result;` into `throw new OperationException(result)`.
- **(4)** lifts a bare `TValue` into a successful `Ok` result — `return order;`.
- **(5)** is the keystone: `OperationFailure` (the type every failure factory returns)
  converts into *any* `OperationResult<TValue>`, so a failure factory can be returned from a
  handler without the caller repeating `TValue`.

## Unwrap semantics

`Unwrap` is the boundary call that asserts success and surfaces the value:

- `OperationResult.Unwrap()` returns `void` — it asserts success and throws
  `OperationException` on failure (via `ThrowIfFailed`).
- `OperationResult<TValue>.Unwrap()` (a `new` override) returns the non-null `Value` on
  success, throws on failure.
- `OperationResultExtensions.UnwrapAsync` are awaitable extensions on
  `Task<OperationResult>` / `Task<OperationResult<TValue>>` that remove the
  `(await …).Unwrap()` parenthesis dance.

```csharp
public static async Task UnwrapAsync(this Task<OperationResult> task);
public static async Task<TValue> UnwrapAsync<TValue>(this Task<OperationResult<TValue>> task);
```

Failure always becomes an `OperationException` carrying the original result, so a caller can
catch it for status-aware handling or let it propagate.

## Factories

All construction goes through static factories on `OperationResult`. Success factories use
method-level generics so `TValue` is inferred (`OperationResult.Ok(order)`, never
`OperationResult<Order>.Ok(...)`). Failure factories return `OperationFailure` — a pure marker
subclass of `OperationResult` whose only job is to carry the implicit conversion (5) onto
`OperationResult<TValue>`.

Every failure factory takes a required `detail` and threads optional named arguments
(`type`, `errors`, `extensions`, `instance`) onto the `OperationProblem`, defaulting `Type`
and `Title` from `OperationStatusExtensions`. The private `Build` helper centralizes that
assembly. `Failed(exception)` captures the exception and uses its `Message` as the detail;
`Failure(status, type, title, detail, …)` is the escape hatch for any status.

## API Surface

### `ArchPillar.Extensions.Operations`

| Type | Kind | Purpose |
| --- | --- | --- |
| `OperationStatus` | `enum : int` | HTTP-aligned status values. |
| `OperationResult` | `class` | Status + optional problem + internal exception. |
| `OperationResult<TValue>` | `sealed class : OperationResult` | Adds a typed `Value`. |
| `OperationProblem` | `sealed class` | RFC 7807 problem body. |
| `OperationError` | `sealed record` | Per-field error item. |
| `OperationFailure` | `sealed class : OperationResult` | Marker for known failures (conversion target). |
| `OperationException` | `sealed class : Exception` | Carries a result through `throw`. |
| `OperationResultExtensions` | `static class` | `UnwrapAsync` extensions. |
| `OperationStatusExtensions` | `static class` | `Type()` / `Title()` defaults. |

### Status checks and unwrap

| Member | Signature |
| --- | --- |
| `OperationResult.IsSuccess` | `bool` (get) |
| `OperationResult.IsFailure` | `bool` (get) |
| `OperationResult.ThrowIfFailed()` | `OperationResult` |
| `OperationResult.Unwrap()` | `void` |
| `OperationResult<TValue>.Unwrap()` | `TValue` |
| `Task<OperationResult>.UnwrapAsync()` | `Task` |
| `Task<OperationResult<TValue>>.UnwrapAsync()` | `Task<TValue>` |

### Success factories

| Member | Returns |
| --- | --- |
| `OperationResult.Ok()` | `OperationResult` |
| `OperationResult.Ok<TValue>(TValue value)` | `OperationResult<TValue>` |
| `OperationResult.Created()` / `Created<TValue>(value)` | `OperationResult` / `OperationResult<TValue>` |
| `OperationResult.Accepted()` / `Accepted<TValue>(value)` | `OperationResult` / `OperationResult<TValue>` |
| `OperationResult.NoContent()` | `OperationResult` |

### Failure factories (all return `OperationFailure`)

| Member | Status | Field errors |
| --- | --- | --- |
| `BadRequest(detail, type?, errors?, extensions?, instance?)` | 400 | yes |
| `Unauthorized(detail, type?, extensions?, instance?)` | 401 | no |
| `Forbidden(detail, type?, extensions?, instance?)` | 403 | no |
| `NotFound(detail, type?, extensions?, instance?)` | 404 | no |
| `Conflict(detail, type?, errors?, extensions?, instance?)` | 409 | yes |
| `Failed(exception, status = InternalServerError)` | 500 | no |
| `Failure(status, type, title, detail, errors?, extensions?, instance?)` | any | yes |

## Error philosophy

- **Failures are values first.** The natural way to report a failure is to *return* the
  factory result; the implicit conversions make that work from any handler signature. `throw`
  is reserved for early exits where threading a return through is noisy.
- **Every failure carries a `detail`.** It is the one required argument on every failure
  factory — the per-occurrence "what's what" message that ends up in the exception message and
  the wire body.
- **The wire body is RFC 7807.** Failures are not opaque strings; they serialize to
  problem-details that an HTTP client or middleware already understands.
- **`Exception` is diagnostic, never wire.** It is `[JsonIgnore]`d and exists so a 500 can
  retain its cause for logging without leaking it to callers.
- **Unwrap fails loud.** Reaching for the value of a failed result throws — there is no silent
  `default`.

## What this library deliberately does not do

- It does not provide functional combinators (`Map`, `Bind`, `Match`) — outcomes are branched
  on, not piped.
- It does not allow an arbitrary error type — the error channel is the fixed `OperationProblem`
  shape.
- It does not depend on or reference ASP.NET Core — mapping a result to an `IResult` /
  `ActionResult` is the host's job, made trivial by the HTTP-aligned status.
- It does not auto-generate or localize problem titles beyond the canonical `Type()` / `Title()`
  defaults.
- It does not persist typed ids itself — EF Core conventions live in the opt-in
  `ArchPillar.Extensions.Primitives.EntityFrameworkCore` package, documented as a
  [feature](../features.md#ef-core-integration-archpillarextensionsprimitivesentityframeworkcore)
  of this library.
