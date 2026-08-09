namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// Formats and parses a <see cref="SourceReference"/> as the <c>path:line:column</c> text used by the
/// container formats. Parsing splits from the right so paths containing a colon (for example Windows
/// drive letters) survive.
/// <para>
/// A reference with no line (line 0) is written as the bare <c>path</c> — the form extraction produces, since a
/// catalog records the files a string is used in, not the lines (Decision D-N), and the gettext <c>#:</c> channel
/// accepts a path alone. A <c>path:line:column</c> read from a file authored elsewhere round-trips unchanged.
/// </para>
/// </summary>
internal static class SourceReferenceText
{
    public static string Format(SourceReference reference) =>
        reference.Line <= 0
            ? reference.FilePath
            : $"{reference.FilePath}:{reference.Line}:{reference.Column}";

    public static SourceReference? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var lastColon = text.LastIndexOf(':');
        if (lastColon <= 0)
        {
            // No line/column suffix: the whole text is the path (our own extraction output, and gettext's
            // path-only reference form).
            return new SourceReference(text, 0, 0);
        }

        var previousColon = text.LastIndexOf(':', lastColon - 1);
        if (previousColon <= 0
            || !int.TryParse(text[(previousColon + 1)..lastColon], out var line)
            || !int.TryParse(text[(lastColon + 1)..], out var column))
        {
            return null;
        }

        return new SourceReference(text[..previousColon], line, column);
    }
}
