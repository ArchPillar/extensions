using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace ArchPillar.Extensions.Localization.AspNetCore;

/// <summary>
/// Teaches MVC to read <see cref="LocalizedAttribute"/> as a display name and description. The DataAnnotations
/// localizer seam cannot do this on its own: MVC asks it to translate only the strings it already found on a
/// system attribute, so a member carrying just <c>[Localized]</c> would silently fall back to its property name.
/// This provider fills the metadata directly, resolving through the ambient store under the declaring type's
/// category — the same <c>(category, key, default)</c> the extractor wrote and the reflection helpers read, so
/// <c>[Localized]</c> and <c>[Display]</c> behave identically in a view.
/// </summary>
/// <remarks>Initializes a new instance resolving each category through <paramref name="localizerForCategory"/>
/// (the ambient store in production, an isolated context in a test).</remarks>
/// <param name="localizerForCategory">Supplies the localizer for a category name.</param>
internal sealed class LocalizedDisplayMetadataProvider(Func<string, ILocalizer> localizerForCategory) : IDisplayMetadataProvider
{
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A property's own attributes, or a type's own — never the merged set, so a [Localized] on a model class
        // does not also label every property whose type happens to be that class.
        var isProperty = context.PropertyAttributes is not null;
        IReadOnlyList<object> attributes = (isProperty ? context.PropertyAttributes : context.TypeAttributes) ?? [];
        if (attributes.OfType<LocalizedAttribute>().FirstOrDefault() is not { } localized)
        {
            return;
        }

        Type? category = isProperty ? context.Key.ContainerType : context.Key.ModelType;
        if (category is null)
        {
            return;
        }

        ILocalizer localizer = localizerForCategory(CategoryName.Of(category));

        // Metadata is cached, so the resolution has to happen inside the delegate: it runs per render, under that
        // request's UI culture.
        context.DisplayMetadata.DisplayName = () => localizer.Translate(localized.Key, localized.Default);
        if (localized.Description is { } description)
        {
            var descriptionKey = localized.DescriptionKey ?? description;
            context.DisplayMetadata.Description = () => localizer.Translate(descriptionKey, description);
        }
    }
}
