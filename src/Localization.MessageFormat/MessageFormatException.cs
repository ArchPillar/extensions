namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Thrown when an ICU MessageFormat string cannot be parsed. Carries the character offset of the
/// failure via <see cref="Position"/>.
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
