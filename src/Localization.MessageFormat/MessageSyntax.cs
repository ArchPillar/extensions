namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// The public syntax surface over ICU MessageFormat strings: validation and placeholder extraction.
/// The parsed representation is an internal implementation detail, so consumers work with strings and
/// results rather than a syntax tree.
/// </summary>
public static class MessageSyntax
{
    /// <summary>
    /// Validates that <paramref name="text"/> is well-formed ICU MessageFormat syntax.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <param name="error">The structured error when the text is invalid; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is valid; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static bool TryValidate(string text, out MessageFormatError? error) =>
        MessageParser.TryParse(text, out _, out error);

    /// <summary>
    /// Returns the distinct argument names referenced anywhere in <paramref name="text"/>, in the order
    /// first encountered, including the selecting argument of every <c>plural</c>/<c>select</c> construct
    /// and any arguments used only inside nested branches.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <returns>The referenced argument names, in first-seen order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="MessageFormatException">The text is not valid ICU MessageFormat syntax.</exception>
    public static IReadOnlyCollection<string> ExtractPlaceholders(string text)
    {
        Message message = MessageParser.Parse(text);
        return MessageParser.ExtractPlaceholders(message);
    }
}
