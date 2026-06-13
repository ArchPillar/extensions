namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// Controls how the formatter behaves when a message references an argument for which no value was
/// supplied.
/// </summary>
public enum MissingArgumentPolicy
{
    /// <summary>
    /// Render the placeholder name in braces unchanged (for example <c>{name}</c>) and continue, so a
    /// missing runtime argument never throws. This is the default.
    /// </summary>
    PassThrough,

    /// <summary>
    /// Throw a <see cref="MissingArgumentException"/> when a referenced argument is missing.
    /// </summary>
    Throw
}
