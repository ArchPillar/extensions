using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A pseudo-localization source for QA. For its target culture it replaces every ASCII letter of the
/// in-code default with <c>X</c>, copying anything inside braces (ICU placeholders and constructs) through
/// untouched so formatting still works. A string that renders as <c>XXXX</c> is going through the
/// localizer; a string that stays readable is hardcoded or otherwise not translatable — so it is an
/// at-a-glance smoke test of what is and is not translated. It is a test aid, not a real translation.
/// </summary>
public sealed class PseudoLocalizationSource : ITranslationSource
{
    private readonly string _cultureName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PseudoLocalizationSource"/> class for a target culture.
    /// </summary>
    /// <param name="cultureName">The culture name that activates pseudo-localization (for example <c>"qps-ploc"</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="cultureName"/> is <see langword="null"/>.</exception>
    public PseudoLocalizationSource(string cultureName)
    {
        _cultureName = cultureName ?? throw new ArgumentNullException(nameof(cultureName));
    }

    /// <inheritdoc />
    public string? Resolve(CultureInfo culture, string category, string key, string defaultMessage)
    {
        if (culture is null || !string.Equals(culture.Name, _cultureName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Pseudo(defaultMessage);
    }

    private static string Pseudo(string message)
    {
        var builder = new StringBuilder(message.Length);
        var depth = 0;
        foreach (var character in message)
        {
            if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && depth > 0)
            {
                depth--;
            }

            builder.Append(depth == 0 && IsAsciiLetter(character) ? 'X' : character);
        }

        return builder.ToString();
    }

    private static bool IsAsciiLetter(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
