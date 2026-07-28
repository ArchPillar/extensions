using Mono.Cecil;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Recovers translatable strings carried by display annotations on a module's own types — type, property, and
/// field (enum member) <c>[DisplayName]</c> / <c>[Display]</c> / <c>[Description]</c>, plus the <c>[Localized…]</c>
/// twins that override the key and default. Separate from <see cref="CallSiteExtractor"/> (which reads IL call
/// sites) so a project can opt out of annotation extraction independently. The category is the declaring type's
/// full name (a member's own enclosing type, a type's own name) — the reflection consumer's view.
/// </summary>
internal static class AnnotationExtractor
{
    private const string DisplayNameAttribute = "System.ComponentModel.DisplayNameAttribute";
    private const string DescriptionAttribute = "System.ComponentModel.DescriptionAttribute";
    private const string DisplayAttribute = "System.ComponentModel.DataAnnotations.DisplayAttribute";
    private const string LocalizedDisplayNameAttribute = "ArchPillar.Extensions.Localization.LocalizedDisplayNameAttribute";
    private const string LocalizedDescriptionAttribute = "ArchPillar.Extensions.Localization.LocalizedDescriptionAttribute";
    private const string LocalizedMessageAttribute = "ArchPillar.Extensions.Localization.LocalizedMessageAttribute`1";

    public static IReadOnlyList<RawCallSite> Extract(ModuleDefinition module)
    {
        var sites = new List<RawCallSite>();
        foreach (TypeDefinition type in AssemblyModuleReader.AllTypes(module))
        {
            var category = ReflectionName(type);
            AddAnnotationSites(type, category, sites);
            foreach (PropertyDefinition property in type.Properties)
            {
                AddAnnotationSites(property, category, sites);
            }

            foreach (FieldDefinition field in type.Fields)
            {
                AddAnnotationSites(field, category, sites);
            }
        }

        return sites;
    }

    // Cecil joins a nested type with '/', reflection's Type.FullName (which the runtime helper resolves the
    // category through) with '+'. Normalize here so an annotation on a nested type's member matches its lookup.
    private static string ReflectionName(TypeDefinition type) => type.FullName.Replace('/', '+');

    // Emits the display-name and description sites a member's attributes carry. A [Localized…] twin overrides
    // the system attribute for its concept (a stable key and a clean default); otherwise the system attribute's
    // literal is both key and default — the .resx-style text-as-key the framework already looks up by.
    private static void AddAnnotationSites(ICustomAttributeProvider member, string category, List<RawCallSite> sites)
    {
        // Display-name concept: [DisplayName("…")] or [Display(Name = "…")]. Description concept: [Description("…")]
        // or [Display(Description = "…")]. The two are distinct strings, so a member carrying both yields two sites.
        AddConcept(member, category, sites, LocalizedDisplayNameAttribute,
            LiteralFromConstructor(member, DisplayNameAttribute) ?? NamedArgument(member, DisplayAttribute, "Name"));
        AddConcept(member, category, sites, LocalizedDescriptionAttribute,
            LiteralFromConstructor(member, DescriptionAttribute) ?? NamedArgument(member, DisplayAttribute, "Description"));
        AddValidationMessageSites(member, category, sites);
    }

    // Emits one site for a display concept. The system attribute's value is the key (the text-as-key default, or a
    // string id when the author prefers one); a [Localized…] twin, when present, supplies the source default for
    // that key instead of reusing the key as the default. Nothing is emitted when there is no system value (no key).
    private static void AddConcept(ICustomAttributeProvider member, string category, List<RawCallSite> sites, string twinAttribute, string? systemKey)
    {
        if (systemKey is null)
        {
            return;
        }

        sites.Add(new RawCallSite(systemKey, LiteralFromConstructor(member, twinAttribute) ?? systemKey, category));
    }

    // Emits a site per [LocalizedMessage<TValidation>] twin — a member may carry one per validator. The twin
    // supplies the source default; its key is the ErrorMessage of the validator named by the type argument, so the
    // catalog key matches what the framework looks the message up by. A twin whose validator sets no ErrorMessage
    // has no key, and is skipped.
    private static void AddValidationMessageSites(ICustomAttributeProvider member, string category, List<RawCallSite> sites)
    {
        foreach (CustomAttribute attribute in member.CustomAttributes)
        {
            if (attribute.AttributeType is GenericInstanceType generic
                && generic.ElementType.FullName == LocalizedMessageAttribute
                && attribute.ConstructorArguments.Count > 0
                && attribute.ConstructorArguments[0].Value is string defaultMessage
                && NamedArgument(member, generic.GenericArguments[0].FullName, "ErrorMessage") is { } key)
            {
                sites.Add(new RawCallSite(key, defaultMessage, category));
            }
        }
    }

    // The first constructor-argument string of the named attribute on a member, or null when the attribute is
    // absent or carries no string argument — the [DisplayName("Email")] / [Description(...)] value, and equally a
    // [Localized…] twin's single constructor argument (its source default).
    private static string? LiteralFromConstructor(ICustomAttributeProvider member, string attributeFullName)
    {
        foreach (CustomAttribute attribute in member.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == attributeFullName
                && attribute.ConstructorArguments.Count > 0
                && attribute.ConstructorArguments[0].Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    // The named-property string of the named attribute on a member, or null when the attribute is absent or the
    // property is unset (e.g. [Display(Name = "Email")] -> "Email").
    private static string? NamedArgument(ICustomAttributeProvider member, string attributeFullName, string propertyName)
    {
        foreach (CustomAttribute attribute in member.CustomAttributes)
        {
            if (attribute.AttributeType.FullName != attributeFullName)
            {
                continue;
            }

            foreach (CustomAttributeNamedArgument named in attribute.Properties)
            {
                if (named.Name == propertyName && named.Argument.Value is string value)
                {
                    return value;
                }
            }
        }

        return null;
    }
}
