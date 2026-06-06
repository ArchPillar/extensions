namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// A parsed ICU MessageFormat message: an ordered sequence of <see cref="MessagePart"/> values.
/// </summary>
/// <param name="Parts">The ordered parts that compose the message.</param>
public sealed record Message(IReadOnlyList<MessagePart> Parts);

/// <summary>
/// The base type for a single component of a parsed <see cref="Message"/>.
/// </summary>
public abstract record MessagePart;

/// <summary>
/// Literal text, with all ICU apostrophe quoting already resolved.
/// </summary>
/// <param name="Text">The decoded literal text.</param>
public sealed record LiteralPart(string Text) : MessagePart;

/// <summary>
/// A simple or typed argument such as <c>{name}</c>, <c>{name, number}</c>,
/// or <c>{name, date, long}</c>.
/// </summary>
/// <param name="Name">The argument name.</param>
/// <param name="Type">The format type (for example <c>number</c>, <c>date</c>, <c>time</c>), or <see langword="null"/> when none is supplied.</param>
/// <param name="Style">The format style, or <see langword="null"/> when none is supplied.</param>
public sealed record ArgumentPart(string Name, string? Type, string? Style) : MessagePart;

/// <summary>
/// The <c>#</c> token inside a <c>plural</c> or <c>selectordinal</c> branch, which renders the
/// formatted number minus the construct's offset.
/// </summary>
public sealed record PoundPart : MessagePart
{
    /// <summary>The shared singleton instance.</summary>
    public static PoundPart Instance { get; } = new();
}

/// <summary>
/// A <c>plural</c> or <c>selectordinal</c> construct.
/// </summary>
/// <param name="ArgumentName">The name of the numeric argument selecting the branch.</param>
/// <param name="Ordinal"><see langword="true"/> for <c>selectordinal</c>; <see langword="false"/> for <c>plural</c>.</param>
/// <param name="Offset">The <c>offset</c> subtracted from the value before category resolution and <c>#</c> rendering.</param>
/// <param name="Branches">The branches, keyed by selector.</param>
public sealed record PluralPart(
    string ArgumentName,
    bool Ordinal,
    int Offset,
    IReadOnlyDictionary<PluralSelector, Message> Branches) : MessagePart;

/// <summary>
/// A <c>select</c> construct that chooses a branch by exact string match.
/// </summary>
/// <param name="ArgumentName">The name of the argument selecting the branch.</param>
/// <param name="Branches">The branches, keyed by their string selector.</param>
public sealed record SelectPart(
    string ArgumentName,
    IReadOnlyDictionary<string, Message> Branches) : MessagePart;

/// <summary>
/// A selector for a <c>plural</c>/<c>selectordinal</c> branch: either an explicit numeric value
/// (<c>=N</c>) or a CLDR plural <see cref="PluralCategory"/> keyword.
/// </summary>
/// <param name="ExplicitValue">The explicit value for a <c>=N</c> selector, or <see langword="null"/>.</param>
/// <param name="Category">The plural category for a keyword selector, or <see langword="null"/>.</param>
public readonly record struct PluralSelector(int? ExplicitValue, PluralCategory? Category);

/// <summary>
/// The CLDR plural categories.
/// </summary>
public enum PluralCategory
{
    /// <summary>The <c>zero</c> category.</summary>
    Zero,

    /// <summary>The <c>one</c> category.</summary>
    One,

    /// <summary>The <c>two</c> category.</summary>
    Two,

    /// <summary>The <c>few</c> category.</summary>
    Few,

    /// <summary>The <c>many</c> category.</summary>
    Many,

    /// <summary>The <c>other</c> category, required on every construct.</summary>
    Other
}
