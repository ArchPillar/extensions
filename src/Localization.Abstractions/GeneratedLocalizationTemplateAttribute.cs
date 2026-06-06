namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Carries the source-language template the generator extracted at compile time, baked into the output
/// assembly so a build task (or the tool) can write it to disk without the generator doing file I/O.
/// The template content is the ARB serialization, Base64-encoded; the tool converts it to the configured
/// format on extraction.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class GeneratedLocalizationTemplateAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedLocalizationTemplateAttribute"/> class.
    /// </summary>
    /// <param name="format">The configured catalog format the template should be materialized as.</param>
    /// <param name="sourceLanguage">The source language (BCP-47) the in-code defaults are written in.</param>
    /// <param name="templateBase64">The ARB template content, Base64-encoded UTF-8.</param>
    public GeneratedLocalizationTemplateAttribute(string format, string sourceLanguage, string templateBase64)
    {
        Format = format;
        SourceLanguage = sourceLanguage;
        TemplateBase64 = templateBase64;
    }

    /// <summary>Gets the configured catalog format the template should be materialized as.</summary>
    public string Format { get; }

    /// <summary>Gets the source language the in-code defaults are written in.</summary>
    public string SourceLanguage { get; }

    /// <summary>Gets the ARB template content, Base64-encoded UTF-8.</summary>
    public string TemplateBase64 { get; }
}
