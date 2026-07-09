using System.Collections.Concurrent;
using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Formats ICU MessageFormat strings against an argument set and a target culture. Parsing is the
/// cost; each instance caches the parsed form per template string, so repeated formatting of the same
/// template does not re-parse. Instances are safe for concurrent use.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MessageFormatter"/> class.
/// </remarks>
/// <param name="missingArguments">How to handle a referenced argument that has no supplied value.</param>
public sealed class MessageFormatter(
    MissingArgumentPolicy missingArguments = MissingArgumentPolicy.PassThrough)
{
    private readonly ConcurrentDictionary<string, Message> _cache = new(StringComparer.Ordinal);
    private readonly MissingArgumentPolicy _missingArguments = missingArguments;

    /// <summary>
    /// Formats <paramref name="template"/> against named tuple arguments in <paramref name="culture"/>.
    /// A literal-only template returns its text with no allocation.
    /// </summary>
    /// <param name="template">The ICU MessageFormat source.</param>
    /// <param name="culture">The culture used for plural resolution and number/date formatting.</param>
    /// <param name="arguments">The argument values as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Format(string template, CultureInfo culture, params (string Name, object? Value)[] arguments)
    {
        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        Message message = _cache.GetOrAdd(template, MessageParser.Parse);
        return MessageRenderer.Render(message, culture, arguments, _missingArguments);
    }
}
