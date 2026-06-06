namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Thrown by the formatter when a message references an argument for which no value was supplied and
/// the active <see cref="MissingArgumentPolicy"/> is <see cref="MissingArgumentPolicy.Throw"/>.
/// </summary>
public sealed class MissingArgumentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MissingArgumentException"/> class.
    /// </summary>
    /// <param name="argumentName">The name of the missing argument.</param>
    public MissingArgumentException(string argumentName)
        : base($"No value was supplied for argument '{argumentName}'.")
    {
        ArgumentName = argumentName;
    }

    /// <summary>
    /// Gets the name of the missing argument.
    /// </summary>
    public string ArgumentName { get; }
}
