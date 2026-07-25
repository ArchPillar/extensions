using System.Text;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// A message recognized as exactly one top-level ICU cardinal <c>plural</c> with category-keyword branches — the
/// shape a gettext <c>Plural-Forms</c> catalog can represent. <see cref="Branches"/> maps each present category to
/// its branch body as ICU source text.
/// </summary>
/// <param name="ArgumentName">The numeric argument that selects the branch.</param>
/// <param name="Branches">Each present category mapped to its branch body (ICU source text).</param>
public sealed record CardinalPlural(string ArgumentName, IReadOnlyDictionary<PluralCategory, string> Branches);

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

    /// <summary>
    /// Returns the argument names of any <c>plural</c>/<c>selectordinal</c>/<c>select</c> construct in
    /// <paramref name="text"/> that is missing the required <c>other</c> branch. Returns an empty set
    /// when the text is not valid syntax (a separate concern reported by <see cref="TryValidate"/>).
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <returns>The argument names of constructs missing an <c>other</c> branch.</returns>
    public static IReadOnlyCollection<string> FindConstructsMissingOther(string text)
    {
        return MessageParser.TryParse(text, out Message? message, out _)
            ? MessageParser.FindConstructsMissingOther(message!)
            : [];
    }

    /// <summary>
    /// Returns <paramref name="text"/> with an empty <c>other {}</c> branch added to every
    /// <c>plural</c>/<c>selectordinal</c>/<c>select</c> construct that is missing one, leaving every other
    /// character untouched. When the text is not valid ICU MessageFormat syntax it is returned unchanged,
    /// since the construct boundaries cannot be trusted.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <returns>The text with the missing <c>other</c> branches inserted, ready for a translator to fill in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string InsertMissingOtherBranches(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return MessageParser.InsertMissingOtherBranches(text);
    }

    /// <summary>
    /// Recognizes <paramref name="text"/> as exactly one top-level ICU cardinal <c>plural</c> with
    /// category-keyword branches (no <c>selectordinal</c>, no <c>offset</c>, no explicit <c>=N</c> selectors, no
    /// surrounding text), returning its argument name and each branch's ICU body. Anything else — including
    /// invalid syntax — returns <see langword="null"/>, so the caller keeps the message as opaque ICU. Replaces a
    /// hand-rolled grammar scan: this runs the real parser and re-emits each branch with
    /// <see cref="MessageSyntax"/>'s own serializer.
    /// </summary>
    /// <param name="text">The ICU MessageFormat source.</param>
    /// <returns>The recognized cardinal plural, or <see langword="null"/> when the text is not that shape.</returns>
    public static CardinalPlural? RecognizeCardinalPlural(string text)
    {
        if (!MessageParser.TryParse(text, out Message? message, out _)
            || message!.Parts.Count != 1
            || message.Parts[0] is not PluralPart { Ordinal: false, Offset: 0 } plural)
        {
            return null;
        }

        var branches = new Dictionary<PluralCategory, string>();
        foreach (KeyValuePair<PluralSelector, Message> branch in plural.Branches)
        {
            if (branch.Key.Category is not { } category)
            {
                return null;
            }

            branches[category] = MessageWriter.Write(branch.Value);
        }

        return new CardinalPlural(plural.ArgumentName, branches);
    }

    /// <summary>
    /// Builds a top-level ICU cardinal <c>plural</c> from an argument name and its category branch bodies — the
    /// inverse of <see cref="RecognizeCardinalPlural"/>, used to reconstruct ICU from a gettext catalog. Each body
    /// is inserted as-is (it is already the branch's source text).
    /// </summary>
    /// <param name="argumentName">The numeric argument that selects the branch.</param>
    /// <param name="branches">The branch bodies in the order they should appear.</param>
    /// <returns>The ICU MessageFormat <c>plural</c> string.</returns>
    public static string BuildCardinalPlural(string argumentName, IReadOnlyList<(PluralCategory Category, string Body)> branches)
    {
        var builder = new StringBuilder();
        builder.Append('{').Append(argumentName).Append(", plural,");
        foreach ((PluralCategory category, var body) in branches)
        {
            builder.Append(' ').Append(category.Keyword()).Append(" {").Append(body).Append('}');
        }

        builder.Append('}');
        return builder.ToString();
    }
}
