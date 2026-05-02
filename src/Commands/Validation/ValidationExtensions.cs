using System.Text.RegularExpressions;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Composable validation helpers used inside
/// <see cref="ICommandHandler{TCommand}.ValidateAsync"/>. Each helper adds
/// an <see cref="OperationError"/> to the context on failure and returns the
/// context, enabling fluent chaining.
/// </summary>
public static class ValidationExtensions
{
    /// <summary>Adds a <c>required</c> error if <paramref name="value"/> is <c>null</c>.</summary>
    public static IValidationContext NotNull<T>(
        this IValidationContext context,
        T? value,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null)
        {
            context.AddError(new OperationError(
                "required",
                message ?? $"{field} is required.",
                field));
        }

        return context;
    }

    /// <summary>Adds a <c>required</c> error if <paramref name="value"/> is <c>null</c> or empty.</summary>
    public static IValidationContext NotEmpty(
        this IValidationContext context,
        string? value,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(value))
        {
            context.AddError(new OperationError(
                "required",
                message ?? $"{field} is required.",
                field));
        }

        return context;
    }

    /// <summary>Adds a <c>required</c> error if the collection is <c>null</c> or empty.</summary>
    public static IValidationContext NotEmpty<T>(
        this IValidationContext context,
        IReadOnlyCollection<T>? value,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null || value.Count == 0)
        {
            context.AddError(new OperationError(
                "required",
                message ?? $"{field} is required.",
                field));
        }

        return context;
    }

    /// <summary>Adds a <c>blank</c> error if <paramref name="value"/> is <c>null</c>, empty, or whitespace.</summary>
    public static IValidationContext NotBlank(
        this IValidationContext context,
        string? value,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(value))
        {
            context.AddError(new OperationError(
                "blank",
                message ?? $"{field} cannot be blank.",
                field));
        }

        return context;
    }

    /// <summary>Adds an <c>out_of_range</c> error if <paramref name="value"/> is outside <c>[min, max]</c>.</summary>
    public static IValidationContext Range<T>(
        this IValidationContext context,
        T value,
        T min,
        T max,
        string field,
        string? message = null)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            context.AddError(new OperationError(
                "out_of_range",
                message ?? $"{field} must be between {min} and {max}.",
                field,
                new Dictionary<string, object?>
                {
                    ["min"] = min,
                    ["max"] = max,
                }));
        }

        return context;
    }

    /// <summary>Adds a <c>too_long</c> error if <paramref name="value"/> exceeds <paramref name="max"/> characters.</summary>
    public static IValidationContext MaxLength(
        this IValidationContext context,
        string? value,
        int max,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is not null && value.Length > max)
        {
            context.AddError(new OperationError(
                "too_long",
                message ?? $"{field} must be at most {max} characters.",
                field,
                new Dictionary<string, object?> { ["max"] = max }));
        }

        return context;
    }

    /// <summary>Adds a <c>too_short</c> error if <paramref name="value"/> is shorter than <paramref name="min"/> characters.</summary>
    public static IValidationContext MinLength(
        this IValidationContext context,
        string? value,
        int min,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is not null && value.Length < min)
        {
            context.AddError(new OperationError(
                "too_short",
                message ?? $"{field} must be at least {min} characters.",
                field,
                new Dictionary<string, object?> { ["min"] = min }));
        }

        return context;
    }

    /// <summary>Adds an <c>invalid_format</c> error if <paramref name="value"/> does not match <paramref name="pattern"/>.</summary>
    public static IValidationContext Matches(
        this IValidationContext context,
        string? value,
        string pattern,
        string field,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pattern);

        if (value is not null && !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
        {
            context.AddError(new OperationError(
                "invalid_format",
                message ?? $"{field} has an invalid format.",
                field));
        }

        return context;
    }

    /// <summary>Adds an error from <paramref name="condition"/>; if <c>false</c>, no error is added.</summary>
    public static IValidationContext Must(
        this IValidationContext context,
        bool condition,
        string code,
        string message,
        string? field = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!condition)
        {
            context.AddError(new OperationError(code, message, field));
        }

        return context;
    }
}
