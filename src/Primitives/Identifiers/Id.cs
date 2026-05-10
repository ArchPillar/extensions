using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Identifiers;

/// <summary>
/// A phantom-typed identifier wrapping a <see cref="Guid"/>.
/// <typeparamref name="T"/> is a compile-time marker that prevents
/// mixing identifiers of different entity types.
/// </summary>
[DebuggerDisplay("{Value}")]
[JsonConverter(typeof(IdJsonConverterFactory))]
public readonly struct Id<T>
    : IId,
      IEquatable<Id<T>>,
      IComparable<Id<T>>,
      ISpanFormattable,
      ISpanParsable<Id<T>>
{
    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>
    /// Initializes an <see cref="Id{T}"/> wrapping <paramref name="value"/>.
    /// </summary>
    public Id(Guid value)
    {
        Value = value;
    }

    /// <summary>
    /// Returns an <see cref="Id{T}"/> whose <see cref="Value"/> is
    /// <see cref="Guid.Empty"/>. Equivalent to <c>default(Id&lt;T&gt;)</c>.
    /// </summary>
    public static Id<T> Empty => default;

    /// <summary>
    /// Creates a new <see cref="Id{T}"/> with a freshly generated Guid.
    /// Uses <c>Guid.CreateVersion7()</c> on .NET 9+; falls back to
    /// <see cref="Guid.NewGuid()"/> on .NET 8.
    /// </summary>
    public static Id<T> New() => new(CreateGuid());

    /// <summary>Implicitly converts an <see cref="Id{T}"/> to its underlying <see cref="Guid"/>.</summary>
    public static implicit operator Guid(Id<T> id) => id.Value;

    /// <summary>Explicitly wraps a <see cref="Guid"/> in an <see cref="Id{T}"/>.</summary>
    public static explicit operator Id<T>(Guid value) => new(value);

    /// <inheritdoc />
    public bool Equals(Id<T> other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Id<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Id<T> other) => Value.CompareTo(other.Value);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Id<T> left, Id<T> right) => left.Value == right.Value;

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Id<T> left, Id<T> right) => left.Value != right.Value;

    /// <summary>Less-than operator.</summary>
    public static bool operator <(Id<T> left, Id<T> right) => left.Value.CompareTo(right.Value) < 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(Id<T> left, Id<T> right) => left.Value.CompareTo(right.Value) <= 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(Id<T> left, Id<T> right) => left.Value.CompareTo(right.Value) > 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(Id<T> left, Id<T> right) => left.Value.CompareTo(right.Value) >= 0;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider)
        => Value.ToString(format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
        => Value.TryFormat(destination, out charsWritten, format);

    /// <inheritdoc />
    public static Id<T> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse(
        ReadOnlySpan<char> s,
        IFormatProvider? provider,
        out Id<T> result)
    {
        if (Guid.TryParse(s, provider, out Guid guid))
        {
            result = new(guid);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public static Id<T> Parse(string s, IFormatProvider? provider)
        => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out Id<T> result)
    {
        if (Guid.TryParse(s, provider, out Guid guid))
        {
            result = new(guid);
            return true;
        }

        result = default;
        return false;
    }

    private static Guid CreateGuid()
    {
#if NET9_0_OR_GREATER
        return Guid.CreateVersion7();
#else
        return Guid.NewGuid();
#endif
    }
}
