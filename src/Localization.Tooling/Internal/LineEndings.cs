using System.Text;
using System.Text.RegularExpressions;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Resolves the line ending a catalog should be written with. The formats always serialize LF; a repository that
/// wants its catalogs in CRLF declares it the same way it does for every other file type, with an
/// <c>end_of_line</c> in <c>.editorconfig</c>.
/// </summary>
internal static class LineEndings
{
    public const string Lf = "\n";
    public const string Crlf = "\r\n";

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The line ending for <paramref name="path"/>: the <c>end_of_line</c> declared for it in <c>.editorconfig</c>,
    /// or the canonical LF. The declaration is the only authority — a catalog that is checked out with some other
    /// convention is a repository configuration matter, and inferring from the file on disk would silently work
    /// around it forever instead. A file is only ever written when its content changed, so normalizing its line
    /// endings at that point rides along with a diff that was happening anyway.
    /// </summary>
    public static string For(string path) => EditorConfig.EndOfLine(path) ?? Lf;

    /// <summary>Re-encodes LF-serialized bytes to <paramref name="lineEnding"/>, leaving LF content untouched.</summary>
    public static byte[] Apply(byte[] serialized, string lineEnding)
    {
        var normalized = _utf8NoBom.GetString(serialized).Replace(Crlf, Lf, StringComparison.Ordinal);
        return _utf8NoBom.GetBytes(
            string.Equals(lineEnding, Crlf, StringComparison.Ordinal)
                ? normalized.Replace(Lf, Crlf, StringComparison.Ordinal)
                : normalized);
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
