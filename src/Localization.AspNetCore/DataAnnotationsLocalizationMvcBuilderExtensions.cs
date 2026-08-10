using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Routes MVC / Razor Pages DataAnnotations localization through ArchPillar's <see cref="ILocalizer"/> instead of
/// <c>.resx</c>. Display names and validation messages are looked up under the model type's category — by the
/// literal as key, or, where a member carries a <c>[Localized…]</c> twin, by the twin's stable key.
/// </summary>
public static class DataAnnotationsLocalizationMvcBuilderExtensions
{
    /// <summary>
    /// Enables DataAnnotations localization and points its localizer provider at ArchPillar, so a model's
    /// <c>[Display]</c> / <c>[DisplayName]</c> / <c>[Description]</c> values and validation <c>ErrorMessage</c>s
    /// resolve through the ambient store under the model type's category. Configure the ambient store as usual
    /// (<c>AddArchPillarLocalization</c> or an ambient catalog load); this only wires the MVC seam.
    /// </summary>
    /// <param name="builder">The MVC builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMvcBuilder AddArchPillarDataAnnotationsLocalization(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The MVC metadata provider invokes DataAnnotationLocalizerProvider only when an IStringLocalizerFactory
        // is also registered; AddLocalization registers it (we hand the provider our own localizer and ignore the
        // factory it is passed). AddLocalization is idempotent, so a prior call is harmless.
        builder.Services.AddLocalization();
        builder.AddDataAnnotationsLocalization(options =>
            options.DataAnnotationLocalizerProvider = (type, _) =>
                new ArchPillarDataAnnotationsLocalizer(Localizer.ForCategory(CategoryName.Of(type)), type));

        // The seam above only translates strings MVC already found on a system attribute, so [Localized] — which
        // needs no system attribute — would be invisible to a view without this provider.
        builder.Services.Configure<MvcOptions>(options =>
            options.ModelMetadataDetailsProviders.Add(new LocalizedDisplayMetadataProvider(Localizer.ForCategory)));
        return builder;
    }
}
