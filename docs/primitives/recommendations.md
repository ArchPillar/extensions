# Recommendations

Production patterns for `ArchPillar.Extensions.Primitives`. Each section is an imperative with
a short rationale, an example, and `>` callouts for the anti-patterns.

## Prefer factories over constructors

Construct results through the static factories on `OperationResult`, never with `new`. The
factories set the status, default the RFC 7807 `Type` / `Title` from the status, and — for
failures — return the `OperationFailure` marker that makes the implicit conversions work. A
hand-built result is easy to get subtly wrong (an unset `Status` defaults to `None`, which is
a *failure*).

```csharp
// Good
return OperationResult.Ok(order);
return OperationResult.NotFound($"Order '{id}' was not found.");

// Avoid — Status defaults to None (a failure), Problem.Type/Title are unset
return new OperationResult<Order> { Value = order };
```

> The public constructors exist for serialization and testing. Treat them as infrastructure,
> not as a call-site API.

## Unwrap at the edge; inspect Status in the middle

`Unwrap` / `UnwrapAsync` throw on failure — they belong at the boundary where you have decided
a failure is exceptional (a controller action that lets an exception filter format the
response, or a top-level orchestration). In code that needs to *react* to specific outcomes —
fall back, retry, branch — inspect `Status` (or `IsSuccess`) instead of catching an exception.

```csharp
// Boundary: throwing is fine — a middleware maps OperationException back to a response
Order order = await dispatcher.SendAsync(getOrder).UnwrapAsync();

// Mid-flow: branch on the outcome, don't throw-and-catch
OperationResult<Reservation> reservation = await inventory.ReserveAsync(orderId);
if (reservation.Status == OperationStatus.Conflict)
{
    return await WaitlistAsync(orderId);   // recover without an exception
}
Reservation reserved = reservation.Unwrap();
```

> Don't `try { result.Unwrap(); } catch (OperationException ex) { … }` as a routine control
> path. If you are catching to read `ex.Result.Status`, you wanted a `Status` check in the
> first place.

## Return OperationResult across HTTP boundaries

The status *is* the HTTP code and the problem body *is* RFC 7807, so map a result to a response
without a translation layer: `(int)result.Status` is the status code and `result.Problem`
serializes as `application/problem+json`. Keep the result type out of your HTTP framework
choice — Primitives is HTTP-*shaped*, not HTTP-*coupled*.

```csharp
// Minimal API
app.MapGet("/orders/{id:guid}", (Guid id, OrderService service) =>
{
    OperationResult<Order> result = service.GetOrder(id);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(detail: result.Problem?.Detail, statusCode: (int)result.Status);
});
```

> Don't invent a parallel "API error" DTO and map `OperationProblem` into it field by field.
> The shape is already the wire contract — serialize it, or feed `Problem` straight into your
> framework's problem-details helper.

## Always supply a meaningful detail

`detail` is the one required argument on every failure factory because it is the
per-occurrence message — it surfaces in the `OperationException.Message`, the logs, and the
wire body. Make it specific (include the offending id or value); leave the generic class
summary to the auto-defaulted `Title`.

```csharp
// Good — names what went wrong
return OperationResult.NotFound($"Order '{id}' was not found.");

// Weak — restates the title, tells the caller nothing
return OperationResult.NotFound("Not found.");
```

## Use the internal Exception for cause, not for the wire

`OperationResult.Failed(exception)` captures the exception into the `[JsonIgnore]`d
`Exception` property — preserved for logging and threaded as the `InnerException` of any
`OperationException`, but never serialized. Reach for it on a 500 path so the cause survives;
don't try to surface it to callers.

```csharp
try
{
    await gateway.ChargeAsync(order);
}
catch (PaymentGatewayException ex)
{
    return OperationResult.Failed(ex);   // 500; ex is retained for logs, not the wire
}
```

> Don't copy an exception's message into `detail` for an internal failure and expect callers
> to act on it. Stack traces and gateway internals are diagnostic — keep them in `Exception`.

## Know the implicit-conversion gotchas

The implicit conversions are what keep call sites terse, but three of them surprise people:

- **`TValue -> OperationResult<TValue>` always means success.** Returning a bare value
  produces an `Ok` result. If `TValue` is itself a reference type that could be `null`, a
  `return maybeNull;` yields a *successful* result wrapping `null` — guard first and return a
  failure factory instead.
- **`throw result;` only compiles for a failure you intend to throw.** The conversion to
  `Exception` happens for any result, including a success — `throw OperationResult.Ok();` is
  legal and throws an `OperationException` over a 200. Throw failures only.
- **`OperationFailure` converts to `OperationResult<T>`, but a plain `OperationResult` does
  not.** Failure factories return `OperationFailure` precisely so the lift works; if you widen
  a failure to the `OperationResult` base type (e.g. a local typed `OperationResult x = …`),
  you lose the conversion and can no longer `return` it where an `OperationResult<T>` is
  expected.

```csharp
// Gotcha: this is a SUCCESS wrapping null, not a failure
public OperationResult<Order> Get(Guid id) => repository.Find(id);   // returns Ok(null) when missing!

// Fix: branch explicitly
public OperationResult<Order> Get(Guid id)
    => repository.Find(id) is { } order ? OperationResult.Ok(order) : OperationResult.NotFound("missing");
```

> If a method can legitimately return "no value but still success", model it with the
> non-generic `OperationResult` (`Ok()` / `NoContent()`), not an `OperationResult<T>` over a
> nullable.

## Opt typed ids into EF Core once, at the DbContext

When you persist `Id<T>` properties, register the convention once with
`UseArchPillarTypedIds()` on the context options rather than configuring each property by hand
— the convention covers every current and future `Id<T>` property, including nullable ones,
and adds the relational type mapping that makes typed ids translate inside arbitrary LINQ
queries. Reserve `HasIdConversion()` for the rare property that needs surgical control or a
context where the global convention is off. See
[features — EF Core integration](features.md#ef-core-integration-archpillarextensionsprimitivesentityframeworkcore).

```csharp
options.UseNpgsql(connectionString).UseArchPillarTypedIds();
```

> Don't mix the two approaches inconsistently across a model. If the convention is on, calling
> `HasIdConversion()` on a property is redundant (the convention skips properties that already
> have a converter); if it's off, every typed-id property needs the explicit call or it won't
> map.
