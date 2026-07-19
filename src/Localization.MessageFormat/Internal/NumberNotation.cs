namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>How a <see cref="NumberFormatSpec"/> scales and abbreviates a value.</summary>
internal enum NumberNotation
{
    /// <summary>Full-length notation (the default).</summary>
    Standard,

    /// <summary>Short compact notation (for example <c>1.2K</c>).</summary>
    CompactShort,

    /// <summary>Long compact notation (for example <c>1.2 thousand</c>).</summary>
    CompactLong
}
