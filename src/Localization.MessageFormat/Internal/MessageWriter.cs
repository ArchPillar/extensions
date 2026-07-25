using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Serializes a parsed <see cref="Message"/> back to ICU MessageFormat text — the inverse of
/// <see cref="MessageGrammarParser"/>. It re-quotes literal syntax characters, so the output parses back to an
/// equivalent message. The one owner of ICU emission, so callers that need a branch's source text (the gettext
/// conversion) do not re-scan the grammar themselves.
/// </summary>
internal static class MessageWriter
{
    public static string Write(Message message)
    {
        var builder = new StringBuilder();
        Write(builder, message);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, Message message)
    {
        foreach (MessagePart part in message.Parts)
        {
            switch (part)
            {
                case LiteralPart literal:
                    WriteLiteral(builder, literal.Text);
                    break;
                case ArgumentPart argument:
                    WriteArgument(builder, argument);
                    break;
                case PoundPart:
                    builder.Append('#');
                    break;
                case PluralPart plural:
                    WritePlural(builder, plural);
                    break;
                case SelectPart select:
                    WriteSelect(builder, select);
                    break;
                default:
                    break;
            }
        }
    }

    private static void WriteArgument(StringBuilder builder, ArgumentPart argument)
    {
        builder.Append('{').Append(argument.Name);
        if (argument.Type is not null)
        {
            builder.Append(", ").Append(argument.Type);
            if (argument.Style is not null)
            {
                builder.Append(", ").Append(argument.Style);
            }
        }

        builder.Append('}');
    }

    private static void WritePlural(StringBuilder builder, PluralPart plural)
    {
        builder.Append('{').Append(plural.ArgumentName).Append(plural.Ordinal ? ", selectordinal," : ", plural,");
        if (plural.Offset != 0)
        {
            builder.Append(" offset:").Append(plural.Offset.ToString(CultureInfo.InvariantCulture));
        }

        foreach (KeyValuePair<PluralSelector, Message> branch in plural.Branches)
        {
            builder.Append(' ');
            if (branch.Key.ExplicitValue is { } value)
            {
                builder.Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(branch.Key.Category!.Value.Keyword());
            }

            builder.Append(" {");
            Write(builder, branch.Value);
            builder.Append('}');
        }

        builder.Append('}');
    }

    private static void WriteSelect(StringBuilder builder, SelectPart select)
    {
        builder.Append('{').Append(select.ArgumentName).Append(", select,");
        foreach (KeyValuePair<string, Message> branch in select.Branches)
        {
            builder.Append(' ').Append(branch.Key).Append(" {");
            Write(builder, branch.Value);
            builder.Append('}');
        }

        builder.Append('}');
    }

    // Re-quotes ICU syntax so the decoded literal round-trips: an apostrophe doubles ('' -> '), and a run of
    // '{', '}', '#' is wrapped in one pair of apostrophes (quoting each separately would let adjacent apostrophes
    // merge into a literal one). Quoting '#' everywhere is safe — outside a plural it decodes to the same '#'.
    private static void WriteLiteral(StringBuilder builder, string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '\'')
            {
                builder.Append("''");
                index++;
            }
            else if (current is '{' or '}' or '#')
            {
                builder.Append('\'');
                while (index < text.Length && text[index] is '{' or '}' or '#')
                {
                    builder.Append(text[index]);
                    index++;
                }

                builder.Append('\'');
            }
            else
            {
                builder.Append(current);
                index++;
            }
        }
    }
}
