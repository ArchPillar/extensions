namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Thrown when an ICU MessageFormat string cannot be parsed, or when it cannot be rendered because a
/// supplied argument has the wrong type (for example a non-numeric value for a <c>plural</c>). For a parse
/// failure <see cref="Position"/> carries the character offset; for a render-time error it is <c>-1</c>.
/// </summary>
public sealed class MessageFormatException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageFormatException"/> class.
    /// </summary>
    /// <param name="message">The human-readable description of the error.</param>
    /// <param name="position">The zero-based character offset where the error was detected.</param>
    public MessageFormatException(string message, int position)
        : base(message)
    {
        Position = position;
    }

    /// <summary>
    /// Gets the zero-based character offset into the source text where the error was detected.
    /// </summary>
    public int Position { get; }
}
