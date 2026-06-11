using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// The source-language template the generator bakes into an assembly as a
/// <c>[GeneratedLocalizationTemplate(format, sourceLanguage, base64Arb)]</c> attribute.
/// </summary>
internal sealed record BakedTemplate(string AssemblyName, string Format, string SourceLanguage, string Arb);

/// <summary>
/// Reads the baked localization template from a built assembly via its metadata, without loading the
/// assembly into an execution context — so scanning a whole <c>bin</c> tree (hundreds of unrelated or
/// platform-specific DLLs) is safe and cheap, and an assembly with no template is simply skipped.
/// </summary>
internal static class BakedTemplateReader
{
    private const string AttributeNamespace = "ArchPillar.Extensions.Localization";
    private const string AttributeName = "GeneratedLocalizationTemplateAttribute";

    /// <summary>
    /// Returns the baked template, or <see langword="null"/> when <paramref name="assemblyPath"/> is not a
    /// readable managed assembly or carries no template attribute.
    /// </summary>
    public static BakedTemplate? TryRead(string assemblyPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return null;
            }

            MetadataReader reader = pe.GetMetadataReader();
            foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                if (!IsTemplateAttribute(reader, attribute))
                {
                    continue;
                }

                BlobReader blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1)
                {
                    return null;
                }

                var format = blob.ReadSerializedString() ?? "arb";
                var sourceLanguage = blob.ReadSerializedString() ?? "en";
                var base64 = blob.ReadSerializedString() ?? string.Empty;
                var arb = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return new BakedTemplate(Path.GetFileNameWithoutExtension(assemblyPath), format, sourceLanguage, arb);
            }

            return null;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or InvalidOperationException or FormatException)
        {
            // Not a managed assembly, a ref-only/native DLL, or a malformed attribute blob: skip it rather
            // than fail the whole scan over one unrelated file in the output folder.
            return null;
        }
    }

    private static bool IsTemplateAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            // The attribute type lives in Abstractions, so the constructor is always a cross-assembly
            // MemberReference; a MethodDefinition would be an attribute defined in this assembly.
            return false;
        }

        MemberReference member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (member.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
        return reader.GetString(type.Name) == AttributeName
            && reader.GetString(type.Namespace) == AttributeNamespace;
    }
}
