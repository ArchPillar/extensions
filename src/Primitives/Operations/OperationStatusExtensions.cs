namespace ArchPillar.Extensions.Operations;

/// <summary>
/// Canonical RFC 7807 problem-type identifier and reason-phrase title
/// extension methods on <see cref="OperationStatus"/>. Serves as the single
/// source of truth for the constant strings that should appear on every
/// <see cref="OperationProblem"/> for a given status.
/// </summary>
public static class OperationStatusExtensions
{
    /// <summary>
    /// The canonical RFC 7807 <c>type</c> identifier for this status — a
    /// short snake-case slug. Used as the default <see cref="OperationProblem.Type"/>
    /// by the failure factories on <see cref="OperationResult"/>.
    /// </summary>
    public static string Type(this OperationStatus status) => status switch
    {
        OperationStatus.Ok => "ok",
        OperationStatus.Created => "created",
        OperationStatus.Accepted => "accepted",
        OperationStatus.NoContent => "no_content",
        OperationStatus.BadRequest => "bad_request",
        OperationStatus.Unauthorized => "unauthorized",
        OperationStatus.Forbidden => "forbidden",
        OperationStatus.NotFound => "not_found",
        OperationStatus.Conflict => "conflict",
        OperationStatus.Gone => "gone",
        OperationStatus.PreconditionFailed => "precondition_failed",
        OperationStatus.UnprocessableEntity => "unprocessable_entity",
        OperationStatus.TooManyRequests => "too_many_requests",
        OperationStatus.InternalServerError => "internal_error",
        OperationStatus.NotImplemented => "not_implemented",
        OperationStatus.ServiceUnavailable => "service_unavailable",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// The canonical RFC 7807 <c>title</c> reason phrase for this status.
    /// Constant per status (only varies for localization, per the RFC). Used
    /// as the default <see cref="OperationProblem.Title"/> by the failure
    /// factories on <see cref="OperationResult"/>.
    /// </summary>
    public static string Title(this OperationStatus status) => status switch
    {
        OperationStatus.Ok => "OK",
        OperationStatus.Created => "Created",
        OperationStatus.Accepted => "Accepted",
        OperationStatus.NoContent => "No Content",
        OperationStatus.BadRequest => "Bad Request",
        OperationStatus.Unauthorized => "Unauthorized",
        OperationStatus.Forbidden => "Forbidden",
        OperationStatus.NotFound => "Not Found",
        OperationStatus.Conflict => "Conflict",
        OperationStatus.Gone => "Gone",
        OperationStatus.PreconditionFailed => "Precondition Failed",
        OperationStatus.UnprocessableEntity => "Unprocessable Entity",
        OperationStatus.TooManyRequests => "Too Many Requests",
        OperationStatus.InternalServerError => "Internal Server Error",
        OperationStatus.NotImplemented => "Not Implemented",
        OperationStatus.ServiceUnavailable => "Service Unavailable",
        _ => status.ToString(),
    };
}
