namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Marks the parameter whose argument is the stable symbolic translation key. A call binding a
/// compile-time constant to this parameter is a translation site.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslatableAttribute : Attribute
{
}

/// <summary>
/// Marks the parameter whose argument is the source-language default message (ICU MessageFormat).
/// This in-code default is the runtime source of truth and the terminal fallback.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslationDefaultAttribute : Attribute
{
}

/// <summary>
/// Marks the parameter carrying disambiguation context, distinguishing two otherwise-identical keys
/// or giving a translator extra meaning.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslationContextAttribute : Attribute
{
}

/// <summary>
/// Marks the parameter carrying a translator comment, or is applied to a method to supply a constant
/// comment for all of its call sites.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TranslationCommentAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationCommentAttribute"/> class with no
    /// constant comment (used on a parameter).
    /// </summary>
    public TranslationCommentAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationCommentAttribute"/> class with a
    /// constant comment (used on a method).
    /// </summary>
    /// <param name="comment">The translator comment applied to every call site of the method.</param>
    public TranslationCommentAttribute(string comment)
    {
        Comment = comment;
    }

    /// <summary>
    /// Gets the constant comment, when supplied on a method.
    /// </summary>
    public string? Comment { get; }
}
