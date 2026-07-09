using System.Text;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Parses ICU MessageFormat strings into the internal <see cref="Message"/> tree and extracts the
/// set of argument names a message references. This is the single source of "what a message means"
/// shared by the validator, the formatter, the format providers, and the runtime; the tree it
/// produces is an implementation detail not exposed to consumers (see <see cref="MessageSyntax"/>).
/// </summary>
internal static class MessageParser
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
    /// Returns <paramref name="text"/> with an empty <c>other {}</c> branch spliced into every
    /// <c>plural</c>/<c>selectordinal</c>/<c>select</c> construct missing one, or unchanged when the text
    /// is not valid syntax. The insertion points come from the parser's own scan, so brace and
    /// apostrophe-quoting rules cannot drift from the grammar.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source (assumed non-<see langword="null"/>).</param>
    /// <returns>The text with the missing <c>other</c> branches inserted.</returns>
    public static string InsertMissingOtherBranches(string text)
    {
        const string OtherBranch = " other {}";

        var parser = new MessageGrammarParser(text);
        try
        {
            parser.ParseFull();
        }
        catch (MessageFormatException)
        {
            return text;
        }

        // Offsets arrive in ascending position order; insert from the last so each splice leaves the
        // earlier offsets valid.
        IReadOnlyList<int> offsets = parser.MissingOtherCloseOffsets;
        if (offsets.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        for (var i = offsets.Count - 1; i >= 0; i--)
        {
            builder.Insert(offsets[i], OtherBranch);
        }

        return builder.ToString();
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
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(message, part =>
        {
            switch (part)
            {
                case ArgumentPart argument:
                    Add(argument.Name, names, seen);
                    break;
                case PluralPart plural:
                    Add(plural.ArgumentName, names, seen);
                    break;
                case SelectPart select:
                    Add(select.ArgumentName, names, seen);
                    break;
                default:
                    break;
            }
        });
        return names;
    }

    /// <summary>
    /// The argument names of every <c>plural</c>/<c>select</c> construct in <paramref name="message"/> that is
    /// missing its required <c>other</c> branch, in first-seen order — what the analyzer flags and the code fix
    /// repairs.
    /// </summary>
    /// <param name="message">The parsed message to inspect.</param>
    /// <returns>The argument names of constructs missing an <c>other</c> branch.</returns>
    public static IReadOnlyCollection<string> FindConstructsMissingOther(Message message)
    {
        var names = new List<string>();
        Walk(message, part =>
        {
            switch (part)
            {
                case PluralPart plural when !PluralSelectors.ContainsOther(plural.Branches.Keys):
                    names.Add(plural.ArgumentName);
                    break;
                case SelectPart select when !select.Branches.ContainsKey("other"):
                    names.Add(select.ArgumentName);
                    break;
                default:
                    break;
            }
        });
        return names;
    }

    // The one owner of "how to walk a Message tree": visits every part in first-seen (pre-order) order,
    // descending into the branch bodies of plural/select constructs, and hands each part to the caller's
    // action. Off the render hot path (used by the extractor and the analyzer), so the delegate's
    // allocation is acceptable — the renderer walks the tree itself to stay allocation-free.
    private static void Walk(Message message, Action<MessagePart> visit)
    {
        foreach (MessagePart part in message.Parts)
        {
            visit(part);
            switch (part)
            {
                case PluralPart plural:
                    foreach (Message branch in plural.Branches.Values)
                    {
                        Walk(branch, visit);
                    }

                    break;
                case SelectPart select:
                    foreach (Message branch in select.Branches.Values)
                    {
                        Walk(branch, visit);
                    }

                    break;
                default:
                    break;
            }
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
