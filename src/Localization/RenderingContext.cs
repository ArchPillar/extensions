using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The shared rendering context for translation: the source culture the in-code defaults are written in, the
/// missing-argument policy, and the ICU <see cref="MessageFormatter"/> (with its parse cache). One context is
/// shared by every localizer over a store, so a template is parsed once and the formatter instance is not
/// rebuilt as catalogs or configuration change. <see cref="Default"/> is the shared <c>en</c> / pass-through
/// context, reused wherever the configuration is the default so those localizers share one formatter.
/// </summary>
internal sealed class RenderingContext
{
    /// <summary>The shared default context: source culture <c>en</c>, <see cref="MissingArgumentPolicy.PassThrough"/>.</summary>
    public static RenderingContext Default { get; } = new();

    /// <summary>
    /// Initializes a new <see cref="RenderingContext"/>.
    /// </summary>
    /// <param name="sourceCulture">The language the in-code defaults are written in; defaults to <c>en</c>.</param>
    /// <param name="missingArguments">How a referenced argument with no supplied value renders.</param>
    public RenderingContext(string sourceCulture = "en", MissingArgumentPolicy missingArguments = MissingArgumentPolicy.PassThrough)
    {
        SourceCultureName = sourceCulture ?? "en";
        SourceCulture = CreateCulture(SourceCultureName);
        MissingArguments = missingArguments;
        Formatter = new MessageFormatter(missingArguments);
    }

    /// <summary>The language the in-code defaults are written in (the source culture's name).</summary>
    public string SourceCultureName { get; }

    /// <summary>The policy applied when a rendered message references an argument with no supplied value.</summary>
    public MissingArgumentPolicy MissingArguments { get; }

    /// <summary>The source culture used to render in-code defaults (their text is in this language).</summary>
    internal CultureInfo SourceCulture { get; }

    /// <summary>The shared ICU formatter and its parse cache.</summary>
    internal MessageFormatter Formatter { get; }

    // Returns the shared Default when the configuration matches it, so default-configured stores and isolated
    // localizers share one formatter instead of each building their own.
    internal static RenderingContext For(string? sourceCulture, MissingArgumentPolicy missingArguments)
    {
        var name = sourceCulture ?? "en";
        return name == "en" && missingArguments == MissingArgumentPolicy.PassThrough
            ? Default
            : new RenderingContext(name, missingArguments);
    }

    private static CultureInfo CreateCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
