using System.Collections.Concurrent;
using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Formats ICU MessageFormat strings against an argument set and a target culture. Parsing is the
/// cost; each instance caches the parsed form per template string, so repeated formatting of the same
/// template does not re-parse. Instances are safe for concurrent use.
/// </summary>
public sealed class MessageFormatter
{
    private readonly ConcurrentDictionary<string, Message> _cache = new(StringComparer.Ordinal);
    private readonly MissingArgumentPolicy _missingArguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageFormatter"/> class.
    /// </summary>
    /// <param name="missingArguments">How to handle a referenced argument that has no supplied value.</param>
    public MessageFormatter(MissingArgumentPolicy missingArguments = MissingArgumentPolicy.PassThrough)
    {
        _missingArguments = missingArguments;
    }

    /// <summary>
    /// Formats <paramref name="template"/> against <paramref name="arguments"/> in
    /// <paramref name="culture"/>.
    /// </summary>
    /// <param name="template">The ICU MessageFormat source.</param>
    /// <param name="culture">The culture used for plural resolution and number/date formatting.</param>
    /// <param name="arguments">The argument values, keyed by name.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="MessageFormatException"><paramref name="template"/> is not valid ICU MessageFormat syntax.</exception>
    /// <exception cref="MissingArgumentException">A referenced argument is missing and the policy is <see cref="MissingArgumentPolicy.Throw"/>.</exception>
    public string Format(string template, CultureInfo culture, IReadOnlyDictionary<string, object?> arguments)
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

    /// <summary>
    /// Formats <paramref name="template"/> against named tuple arguments in <paramref name="culture"/>.
    /// </summary>
    /// <param name="template">The ICU MessageFormat source.</param>
    /// <param name="culture">The culture used for plural resolution and number/date formatting.</param>
    /// <param name="arguments">The argument values as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Format(string template, CultureInfo culture, params (string Name, object? Value)[] arguments)
    {
        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        return Format(template, culture, ToDictionary(arguments));
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary((string Name, object? Value)[] arguments)
    {
        var map = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach ((var name, var value) in arguments)
        {
            map[name] = value;
        }

        return map;
    }
}
