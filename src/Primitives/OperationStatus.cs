namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// HTTP-aligned status values carried by <see cref="OperationResult"/>. Numeric
/// values match the corresponding HTTP status codes so a result can be mapped
/// directly to an HTTP response without translation.
/// </summary>
/// <remarks>
/// The set is deliberately narrow — only the codes commonly returned from
/// command handlers. Cast from <see cref="int"/> when a code outside this set
/// is required.
/// </remarks>
public enum OperationStatus
{
    /// <summary>
    /// The default for an unset result. Treated as failure
    /// (<see cref="OperationResult.IsSuccess"/> is <c>false</c>).
    /// </summary>
    None = 0,

    /// <summary>200 OK — the operation succeeded and produced a value.</summary>
    Ok = 200,

    /// <summary>201 Created — the operation created a new resource.</summary>
    Created = 201,

    /// <summary>202 Accepted — the operation has been queued for asynchronous processing.</summary>
    Accepted = 202,

    /// <summary>204 No Content — the operation succeeded with no value to return.</summary>
    NoContent = 204,

    /// <summary>400 Bad Request — the input is syntactically invalid.</summary>
    BadRequest = 400,

    /// <summary>401 Unauthorized — the caller is not authenticated.</summary>
    Unauthorized = 401,

    /// <summary>403 Forbidden — the caller is authenticated but not permitted.</summary>
    Forbidden = 403,

    /// <summary>404 Not Found — a referenced resource does not exist.</summary>
    NotFound = 404,

    /// <summary>409 Conflict — the operation conflicts with the current resource state.</summary>
    Conflict = 409,

    /// <summary>410 Gone — the resource existed but is permanently removed.</summary>
    Gone = 410,

    /// <summary>412 Precondition Failed — an If-Match / If-None-Match condition was not met.</summary>
    PreconditionFailed = 412,

    /// <summary>422 Unprocessable Entity — the input is syntactically valid but semantically rejected (typically validation failures).</summary>
    UnprocessableEntity = 422,

    /// <summary>429 Too Many Requests — the caller has been rate-limited.</summary>
    TooManyRequests = 429,

    /// <summary>500 Internal Server Error — an unhandled error occurred.</summary>
    InternalServerError = 500,

    /// <summary>501 Not Implemented — the operation is not supported.</summary>
    NotImplemented = 501,

    /// <summary>503 Service Unavailable — a dependency is unavailable.</summary>
    ServiceUnavailable = 503,
}
