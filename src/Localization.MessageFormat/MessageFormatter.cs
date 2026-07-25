using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Formats ICU MessageFormat strings against an argument set and a target culture. Parsing is the
/// cost; each instance caches the parse outcome per template string — the parsed form, or the error a
/// malformed template raises — so repeated formatting of the same template never re-parses. Instances
/// are safe for concurrent use.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MessageFormatter"/> class.
/// </remarks>
/// <param name="missingArguments">How to handle a referenced argument that has no supplied value.</param>
public sealed class MessageFormatter(
    MissingArgumentPolicy missingArguments = MissingArgumentPolicy.PassThrough)
{
    private readonly ConcurrentDictionary<string, ParseOutcome> _cache = new(StringComparer.Ordinal);
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

        Message message = _cache.GetOrAdd(template, ParseToOutcome).Resolve();
        return MessageRenderer.Render(message, culture, arguments, _missingArguments);
    }

    // Test seam: reports whether the template cache holds a negatively-cached parse error for
    // <paramref name="template"/> — proof the malformed template was parsed once and will not re-parse.
    internal bool TryGetCachedParseError(string template, out MessageFormatException? error)
    {
        if (_cache.TryGetValue(template, out ParseOutcome outcome) && outcome.Error is { } cached)
        {
            error = cached;
            return true;
        }

        error = null;
        return false;
    }

    // The GetOrAdd value factory: parses the template once and captures the outcome, so a malformed template
    // is parsed a single time and its error is cached rather than re-thrown from a re-parse on every call. Only
    // a parse error is caught — anything else is a bug in Parse and must propagate. Under a first-time race the
    // factory can run more than once, which is harmless: Parse is pure and deterministic, so it yields the same
    // outcome, and the success path stays a plain cache hit returning the Message.
    private static ParseOutcome ParseToOutcome(string template)
    {
        try
        {
            return new(MessageParser.Parse(template), null);
        }
        catch (MessageFormatException error)
        {
            return new(null, error);
        }
    }

    // The cached result of parsing one template: exactly one of Message (success) or Error (malformed) is set.
    private readonly record struct ParseOutcome(Message? Message, MessageFormatException? Error)
    {
        // Reproduces the original parse failure on every cache hit — same exception type, Message, and Position —
        // rethrowing through ExceptionDispatchInfo so the first parse's stack trace is preserved.
        public Message Resolve()
        {
            if (Error is { } error)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            return Message!;
        }
    }
}
