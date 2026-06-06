namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// Formats and parses a <see cref="SourceReference"/> as the <c>path:line:column</c> text used by the
/// container formats. Parsing splits from the right so paths containing a colon (for example Windows
/// drive letters) survive.
/// </summary>
internal static class SourceReferenceText
{
    public static string Format(SourceReference reference) =>
        $"{reference.FilePath}:{reference.Line}:{reference.Column}";

    public static SourceReference? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var lastColon = text!.LastIndexOf(':');
        if (lastColon <= 0)
        {
            return null;
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
