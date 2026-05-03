using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Composable validation helpers used inside
/// <see cref="ICommandHandler{TCommand}.ValidateAsync"/>. Each helper
/// captures the caller's argument expression as the field name (override by
/// passing the parameter explicitly), attaches a per-validator
/// <see cref="OperationStatus"/>, populates structured extensions when
/// applicable, and returns the context for chaining.
/// </summary>
public static class ValidationExtensions
{
    private const string Required = "required";
    private const string Blank = "blank";
    private const string OutOfRange = "out_of_range";
    private const string TooLong = "too_long";
    private const string TooShort = "too_short";
    private const string InvalidFormat = "invalid_format";

    /// <summary>Adds a <c>required</c> error if <paramref name="value"/> is <c>null</c>.</summary>
    public static IValidationContext NotNull<T>(
        this IValidationContext context,
        T? value,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null)
        {
            context.AddError(field, new OperationError(
                Required,
                detail ?? $"{field} is required.",
                OperationStatus.BadRequest));
        }

        return context;
    }

    /// <summary>Adds a <c>required</c> error if <paramref name="value"/> is <c>null</c> or empty.</summary>
    public static IValidationContext NotEmpty(
        this IValidationContext context,
        string? value,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(value))
        {
            context.AddError(field, new OperationError(
                Required,
                detail ?? $"{field} is required.",
                OperationStatus.BadRequest));
        }

        return context;
    }

    /// <summary>Adds a <c>required</c> error if the collection is <c>null</c> or empty.</summary>
    public static IValidationContext NotEmpty<T>(
        this IValidationContext context,
        IReadOnlyCollection<T>? value,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null || value.Count == 0)
        {
            context.AddError(field, new OperationError(
                Required,
                detail ?? $"{field} is required.",
                OperationStatus.BadRequest));
        }

        return context;
    }

    /// <summary>Adds a <c>blank</c> error if <paramref name="value"/> is <c>null</c>, empty, or whitespace.</summary>
    public static IValidationContext NotBlank(
        this IValidationContext context,
        string? value,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(value))
        {
            context.AddError(field, new OperationError(
                Blank,
                detail ?? $"{field} cannot be blank.",
                OperationStatus.BadRequest));
        }

        return context;
    }

    /// <summary>Adds an <c>out_of_range</c> error if <paramref name="value"/> is outside <c>[min, max]</c>.</summary>
    public static IValidationContext Range<T>(
        this IValidationContext context,
        T value,
        T min,
        T max,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            context.AddError(field, new OperationError(
                OutOfRange,
                detail ?? $"{field} must be between {min} and {max}.",
                OperationStatus.BadRequest,
                new Dictionary<string, object?>
                {
                    ["min"] = min,
                    ["max"] = max,
                    ["actual"] = value,
                }));
        }

        return context;
    }

    /// <summary>Adds a <c>too_long</c> error if <paramref name="value"/> exceeds <paramref name="max"/> characters.</summary>
    public static IValidationContext MaxLength(
        this IValidationContext context,
        string? value,
        int max,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is not null && value.Length > max)
        {
            context.AddError(field, new OperationError(
                TooLong,
                detail ?? $"{field} must be at most {max} characters.",
                OperationStatus.BadRequest,
                new Dictionary<string, object?>
                {
                    ["max"] = max,
                    ["length"] = value.Length,
                }));
        }

        return context;
    }

    /// <summary>Adds a <c>too_short</c> error if <paramref name="value"/> is shorter than <paramref name="min"/> characters.</summary>
    public static IValidationContext MinLength(
        this IValidationContext context,
        string? value,
        int min,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is not null && value.Length < min)
        {
            context.AddError(field, new OperationError(
                TooShort,
                detail ?? $"{field} must be at least {min} characters.",
                OperationStatus.BadRequest,
                new Dictionary<string, object?>
                {
                    ["min"] = min,
                    ["length"] = value.Length,
                }));
        }

        return context;
    }

    /// <summary>Adds an <c>invalid_format</c> error if <paramref name="value"/> does not match <paramref name="pattern"/>.</summary>
    public static IValidationContext Matches(
        this IValidationContext context,
        string? value,
        string pattern,
        string? detail = null,
        [CallerArgumentExpression(nameof(value))] string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pattern);

        if (value is not null && !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
        {
            context.AddError(field, new OperationError(
                InvalidFormat,
                detail ?? $"{field} has an invalid format.",
                OperationStatus.BadRequest,
                new Dictionary<string, object?> { ["pattern"] = pattern }));
        }

        return context;
    }

    /// <summary>Adds a <c>not_found</c> error (status 404) if <paramref name="entity"/> is <c>null</c>.</summary>
    public static IValidationContext Exists<T>(
        this IValidationContext context,
        T? entity,
        string? detail = null,
        [CallerArgumentExpression(nameof(entity))] string? field = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        if (entity is null)
        {
            context.AddError(null, new OperationError(
                "not_found",
                detail ?? $"{field ?? typeof(T).Name} not found.",
                OperationStatus.NotFound));
        }

        return context;
    }

    /// <summary>Adds an <c>unauthorized</c> error (status 401) if <paramref name="condition"/> is <c>false</c>.</summary>
    public static IValidationContext Authenticate(
        this IValidationContext context,
        bool condition,
        string? detail = null,
        [CallerArgumentExpression(nameof(condition))] string? rule = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!condition)
        {
            context.AddError(null, new OperationError(
                "unauthorized",
                detail ?? "Authentication is required.",
                OperationStatus.Unauthorized,
                rule is null ? null : new Dictionary<string, object?> { ["rule"] = rule }));
        }

        return context;
    }

    /// <summary>Adds a <c>forbidden</c> error (status 403) if <paramref name="condition"/> is <c>false</c>.</summary>
    public static IValidationContext Authorize(
        this IValidationContext context,
        bool condition,
        string? detail = null,
        [CallerArgumentExpression(nameof(condition))] string? rule = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!condition)
        {
            context.AddError(null, new OperationError(
                "forbidden",
                detail ?? $"Not authorized: {rule}.",
                OperationStatus.Forbidden,
                rule is null ? null : new Dictionary<string, object?> { ["rule"] = rule }));
        }

        return context;
    }

    /// <summary>Adds a <c>conflict</c> error (status 409) if <paramref name="condition"/> is <c>false</c>.</summary>
    public static IValidationContext Conflict(
        this IValidationContext context,
        bool condition,
        string? detail = null,
        [CallerArgumentExpression(nameof(condition))] string? rule = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!condition)
        {
            context.AddError(null, new OperationError(
                "conflict",
                detail ?? "Conflict with current state.",
                OperationStatus.Conflict,
                rule is null ? null : new Dictionary<string, object?> { ["rule"] = rule }));
        }

        return context;
    }

    /// <summary>
    /// General-purpose escape hatch: adds an error with caller-supplied
    /// <paramref name="status"/>, <paramref name="type"/>, and
    /// <paramref name="detail"/> when <paramref name="condition"/> is <c>false</c>.
    /// </summary>
    public static IValidationContext Require(
        this IValidationContext context,
        bool condition,
        OperationStatus status,
        string type,
        string detail,
        string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!condition)
        {
            context.AddError(field, new OperationError(type, detail, status));
        }

        return context;
    }

    /// <summary>
    /// Convenience over <see cref="Require"/> that defaults the status to
    /// <see cref="OperationStatus.BadRequest"/>.
    /// </summary>
    public static IValidationContext Must(
        this IValidationContext context,
        bool condition,
        string type,
        string detail,
        string? field = null)
        => Require(context, condition, OperationStatus.BadRequest, type, detail, field);
}
