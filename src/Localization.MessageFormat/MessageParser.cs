using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Parses ICU MessageFormat strings into a <see cref="Message"/> abstract syntax tree and extracts
/// the set of argument names a message references. This is the single source of "what a message means"
/// shared by the analyzer, the extractor, the format providers, and the runtime.
/// </summary>
public static class MessageParser
{
    /// <summary>
    /// Parses <paramref name="text"/> into a <see cref="Message"/>.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <returns>The parsed message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="MessageFormatException">The text is not valid ICU MessageFormat syntax.</exception>
    public static Message Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return new MessageGrammarParser(text).ParseFull();
    }

    /// <summary>
    /// Attempts to parse <paramref name="text"/> into a <see cref="Message"/> without throwing on
    /// syntax errors.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <param name="message">The parsed message when parsing succeeds; otherwise <see langword="null"/>.</param>
    /// <param name="error">The structured error when parsing fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string text, out Message? message, out MessageFormatError? error)
    {
        try
        {
            message = Parse(text);
            error = null;
            return true;
        }
        catch (MessageFormatException ex)
        {
            message = null;
            error = new MessageFormatError(ex.Message, ex.Position);
            return false;
        }
    }

    /// <summary>
    /// Returns the distinct argument names referenced anywhere in <paramref name="message"/>, in the
    /// order first encountered, including the selecting argument of every <c>plural</c>/<c>select</c>
    /// construct and any arguments used only inside nested branches.
    /// </summary>
    /// <param name="message">The message to inspect.</param>
    /// <returns>The referenced argument names, in first-seen order.</returns>
    public static IReadOnlyCollection<string> ExtractPlaceholders(Message message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectFromMessage(message, names, seen);
        return names;
    }

    private static void CollectFromMessage(Message message, List<string> names, HashSet<string> seen)
    {
        foreach (MessagePart part in message.Parts)
        {
            CollectFromPart(part, names, seen);
        }
    }

    private static void CollectFromPart(MessagePart part, List<string> names, HashSet<string> seen)
    {
        switch (part)
        {
            case ArgumentPart argument:
                Add(argument.Name, names, seen);
                break;
            case PluralPart plural:
                Add(plural.ArgumentName, names, seen);
                CollectFromBranches(plural.Branches.Values, names, seen);
                break;
            case SelectPart select:
                Add(select.ArgumentName, names, seen);
                CollectFromBranches(select.Branches.Values, names, seen);
                break;
            default:
                break;
        }
    }

    private static void CollectFromBranches(IEnumerable<Message> branches, List<string> names, HashSet<string> seen)
    {
        foreach (Message branch in branches)
        {
            CollectFromMessage(branch, names, seen);
        }
    }

    private static void Add(string name, List<string> names, HashSet<string> seen)
    {
        if (seen.Add(name))
        {
            names.Add(name);
        }
    }
}
