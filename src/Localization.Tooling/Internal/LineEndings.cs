using System.Text;
using System.Text.RegularExpressions;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Resolves the line ending a catalog should be written with. The formats always serialize LF; a repository that
/// keeps CRLF on disk (Git's <c>autocrlf</c>, a <c>text</c> attribute, or a declared <c>end_of_line</c>) would
/// otherwise see every written line as changed instead of only the lines that actually changed.
/// </summary>
internal static class LineEndings
{
    public const string Lf = "\n";
    public const string Crlf = "\r\n";

    // Enough of the file to reach its first line break; a catalog's first break is within the first line of markup.
    private const int PeekBytes = 4096;

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The line ending for <paramref name="path"/>. An existing file's own convention wins — matching what is
    /// actually on disk is what keeps a diff limited to the lines that changed, and it is fact rather than
    /// declaration. Otherwise a declared <c>end_of_line</c> from <c>.editorconfig</c> decides how a brand-new
    /// catalog is seeded, and failing that the canonical LF.
    /// </summary>
    public static string For(string path)
    {
        if (File.Exists(path))
        {
            return OnDisk(path);
        }

        return EditorConfig.EndOfLine(path) ?? Lf;
    }

    /// <summary>Re-encodes LF-serialized bytes to <paramref name="lineEnding"/>, leaving LF content untouched.</summary>
    public static byte[] Apply(byte[] serialized, string lineEnding)
    {
        var normalized = _utf8NoBom.GetString(serialized).Replace(Crlf, Lf, StringComparison.Ordinal);
        return _utf8NoBom.GetBytes(
            string.Equals(lineEnding, Crlf, StringComparison.Ordinal)
                ? normalized.Replace(Lf, Crlf, StringComparison.Ordinal)
                : normalized);
    }

    // A bounded peek rather than a full read: only the first line break is needed, and this runs on the write path
    // only, never on a run that changes nothing.
    private static string OnDisk(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var buffer = new byte[PeekBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        var lineFeed = buffer.AsSpan(0, read).IndexOf((byte)'\n');
        return lineFeed > 0 && buffer[lineFeed - 1] == (byte)'\r' ? Crlf : Lf;
    }

    /// <summary>
    /// The <c>end_of_line</c> declaration for a path, read from the <c>.editorconfig</c> chain above it. A minimal
    /// reader: it understands section globs, <c>root</c>, and <c>end_of_line</c>, which is all that is consulted
    /// here — anything else in the file is ignored rather than half-interpreted.
    /// </summary>
    private static class EditorConfig
    {
        public static string? EndOfLine(string path)
        {
            var fileName = Path.GetFileName(path);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));

            // Nearest file wins, so walk upwards and keep the first declaration found.
            while (!string.IsNullOrEmpty(directory))
            {
                var configPath = Path.Combine(directory, ".editorconfig");
                if (File.Exists(configPath))
                {
                    (var value, var root) = Read(configPath, fileName);
                    if (value is not null)
                    {
                        return value;
                    }

                    if (root)
                    {
                        return null;
                    }
                }

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        // The last matching section wins within one file, matching editorconfig precedence.
        private static (string? Value, bool Root) Read(string configPath, string fileName)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(configPath);
            }
            catch (IOException)
            {
                return (null, false);
            }

            string? value = null;
            var root = false;
            var applies = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';')
                {
                    continue;
                }

                if (line[0] == '[' && line[^1] == ']')
                {
                    applies = Matches(line[1..^1], fileName);
                    continue;
                }

                (var key, var setting) = Split(line);
                if (key is null)
                {
                    continue;
                }

                if (!applies && string.Equals(key, "root", StringComparison.OrdinalIgnoreCase))
                {
                    root = string.Equals(setting, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (applies && string.Equals(key, "end_of_line", StringComparison.OrdinalIgnoreCase))
                {
                    value = setting switch
                    {
                        "crlf" => Crlf,
                        "lf" => Lf,
                        _ => value
                    };
                }
            }

            return (value, root);
        }

        private static (string? Key, string Setting) Split(string line)
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            return separator <= 0
                ? (null, "")
                : (line[..separator].Trim(), line[(separator + 1)..].Trim().ToLowerInvariant());
        }

        // Only the glob forms that appear in a real .editorconfig section header are honored: '*', '?', '{a,b}'
        // alternation, and character classes. A section this cannot parse simply does not match.
        private static bool Matches(string pattern, string fileName)
        {
            var builder = new StringBuilder("^");
            foreach (var character in pattern)
            {
                builder.Append(character switch
                {
                    '*' => "[^/]*",
                    '?' => ".",
                    '{' => "(?:",
                    '}' => ")",
                    ',' => "|",
                    '[' => "[",
                    ']' => "]",
                    _ => Regex.Escape(character.ToString())
                });
            }

            builder.Append('$');
            try
            {
                return Regex.IsMatch(fileName, builder.ToString(), RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
