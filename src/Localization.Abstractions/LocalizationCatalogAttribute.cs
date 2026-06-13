namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Advertises a translation catalog embedded in the assembly as a manifest resource, so the ambient
/// localization store can discover and load it when the assembly is loaded — without the host configuring
/// anything. One attribute is emitted per embedded catalog (typically one per shipped language). Discovery
/// is an attribute read, never a manifest-resource scan.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class LocalizationCatalogAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationCatalogAttribute"/> class.
    /// </summary>
    /// <param name="resourceName">The manifest resource name of the embedded catalog.</param>
    /// <param name="format">The catalog format identifier (for example <c>"arb"</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="resourceName"/> or <paramref name="format"/> is <see langword="null"/>.</exception>
    public LocalizationCatalogAttribute(string resourceName, string format)
    {
        ResourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    /// <summary>The manifest resource name of the embedded catalog.</summary>
    public string ResourceName { get; }

    /// <summary>The catalog format identifier.</summary>
    public string Format { get; }
}
