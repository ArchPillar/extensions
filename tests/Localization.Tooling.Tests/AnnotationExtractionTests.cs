using ArchPillar.Extensions.Localization.Tooling.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// Drives the IL extractor's annotation pass against a real, freshly compiled assembly. Display-name and
/// description attributes (<c>[DisplayName]</c>, <c>[Display]</c>, <c>[Description]</c>) carry genuine source
/// text that ASP.NET and other reflection consumers render, so they are extracted by default: the system
/// attribute's literal becomes both key and in-code default, scoped to the declaring type, unless a
/// <c>[Localized…]</c> twin supplies a stable key and a clean default instead.
/// </summary>
public sealed class AnnotationExtractionTests : IDisposable
{
    private readonly List<string> _emitted = [];

    [Fact]
    public void ExtractAnnotations_DisplayNameOnProperty_TakesLiteralAsKeyAndDefaultUnderDeclaringType()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel;

            namespace Demo;

            public sealed class RegisterModel
            {
                [DisplayName("Email address")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("Email address", site.Key);
        Assert.Equal("Email address", site.Default);
        Assert.Equal("Demo.RegisterModel", site.Category);
        Assert.Null(site.Context);
    }

    [Fact]
    public void ExtractAnnotations_DisplayNameOnProperty_TakesTheDisplayAttributeName()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Display(Name = "Email address")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("Email address", site.Key);
        Assert.Equal("Email address", site.Default);
        Assert.Equal("Demo.RegisterModel", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_DescriptionOnProperty_TakesTheLiteral()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Description("The address we send confirmations to.")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("The address we send confirmations to.", site.Key);
        Assert.Equal("The address we send confirmations to.", site.Default);
        Assert.Equal("Demo.RegisterModel", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_DisplayAttributeDescription_TakesTheDescription()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Display(Description = "The address we send confirmations to.")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("The address we send confirmations to.", site.Key);
        Assert.Equal("Demo.RegisterModel", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_NameAndDescriptionOnOneMember_ExtractsBothConcepts()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Display(Name = "Email address", Description = "Where we send confirmations.")]
                public string Email { get; set; }
            }
            """);

        Assert.Equal(2, sites.Count);
        Assert.Contains(sites, site => site.Key == "Email address");
        Assert.Contains(sites, site => site.Key == "Where we send confirmations.");
        Assert.All(sites, site => Assert.Equal("Demo.RegisterModel", site.Category));
    }

    [Fact]
    public void ExtractAnnotations_DisplayOnEnumMembers_TakesCategoryFromTheEnumType()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;

            namespace Demo;

            public enum AccountStatus
            {
                [Display(Name = "Active")]
                Active,

                [Display(Name = "Suspended")]
                Suspended,
            }
            """);

        Assert.Equal(2, sites.Count);
        Assert.All(sites, site => Assert.Equal("Demo.AccountStatus", site.Category));
        Assert.Contains(sites, site => site.Key == "Active");
        Assert.Contains(sites, site => site.Key == "Suspended");
    }

    [Fact]
    public void ExtractAnnotations_DisplayNameOnType_TakesCategoryFromTheTypeItself()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel;

            namespace Demo;

            [DisplayName("User account")]
            public sealed class Account
            {
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("User account", site.Key);
        Assert.Equal("Demo.Account", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_DisplayOnNestedEnumMember_UsesTheReflectionStyleNestedName()
    {
        // The runtime helper computes the category from Type.FullName, which joins a nested type with '+'.
        // Cecil joins with '/', so extraction must normalize or the catalog key never matches the lookup.
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;

            namespace Demo;

            public sealed class Account
            {
                public enum Status
                {
                    [Display(Name = "Active")]
                    Active,
                }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("Active", site.Key);
        Assert.Equal("Demo.Account+Status", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_LocalizedDisplayNameTwin_OverridesWithStableKeyAndDefault()
    {
        // The twin rides beside the system attribute; extraction uses the twin's stable key and default, and does
        // not also emit the literal — one display name, one catalog entry.
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel;
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class RegisterModel
            {
                [DisplayName("Email address")]
                [LocalizedDisplayName("register.email.label", "Email address")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("register.email.label", site.Key);
        Assert.Equal("Email address", site.Default);
        Assert.Equal("Demo.RegisterModel", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_LocalizedDisplayNameTwin_OneArgument_UsesItAsKeyAndDefault()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public enum AccountStatus
            {
                [LocalizedDisplayName("Active")]
                Active,
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("Active", site.Key);
        Assert.Equal("Active", site.Default);
        Assert.Equal("Demo.AccountStatus", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_LocalizedDescriptionTwin_OverridesWithStableKey()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel;
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Description("Where we send confirmations.")]
                [LocalizedDescription("register.email.help", "Where we send confirmations.")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("register.email.help", site.Key);
        Assert.Equal("Where we send confirmations.", site.Default);
    }

    [Fact]
    public void ExtractAnnotations_LocalizedMessageTwin_ExtractsKeyAndDefaultUnderDeclaringType()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Required]
                [LocalizedMessage<RequiredAttribute>("register.email.required", "An email address is required.")]
                public string Email { get; set; }
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("register.email.required", site.Key);
        Assert.Equal("An email address is required.", site.Default);
        Assert.Equal("Demo.RegisterModel", site.Category);
    }

    [Fact]
    public void ExtractAnnotations_MultipleLocalizedMessageTwins_ExtractsOnePerValidator()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            using System.ComponentModel.DataAnnotations;
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class RegisterModel
            {
                [Required]
                [StringLength(100)]
                [LocalizedMessage<RequiredAttribute>("register.email.required", "An email address is required.")]
                [LocalizedMessage<StringLengthAttribute>("register.email.tooLong", "That email is too long.")]
                public string Email { get; set; }
            }
            """);

        Assert.Equal(2, sites.Count);
        Assert.Contains(sites, site => site.Key == "register.email.required");
        Assert.Contains(sites, site => site.Key == "register.email.tooLong");
    }

    [Fact]
    public void ExtractAnnotations_NoAnnotations_IsEmpty()
    {
        IReadOnlyList<RawCallSite> sites = ExtractAnnotations("""
            namespace Demo;

            public sealed class Plain
            {
                public string Name { get; set; }
            }
            """);

        Assert.Empty(sites);
    }

    [Fact]
    public void Build_IncludesAnnotationStringsByDefault()
    {
        // A model with only annotations (no ILocalizer call sites) still yields a template: the annotation pass
        // does not depend on the localizer early-out the IL call-site pass uses.
        var path = Compile("""
            using System.ComponentModel;

            namespace Demo;

            public sealed class RegisterModel
            {
                [DisplayName("Email address")]
                public string Email { get; set; }
            }
            """);

        using var extractor = new AssemblyStringExtractor();
        Catalog template = Assert.IsType<Catalog>(TemplateBuilder.Build(extractor, path, "en"));
        Assert.Contains(template.Entries, entry => entry.Key == "Email address");
    }

    [Fact]
    public void Build_WithAnnotationsExcluded_OmitsAnnotationStrings()
    {
        var path = Compile("""
            using System.ComponentModel;

            namespace Demo;

            public sealed class RegisterModel
            {
                [DisplayName("Email address")]
                public string Email { get; set; }
            }
            """);

        using var extractor = new AssemblyStringExtractor();
        Assert.Null(TemplateBuilder.Build(extractor, path, "en", includeAnnotations: false));
    }

    private IReadOnlyList<RawCallSite> ExtractAnnotations(string source)
    {
        var path = Compile(source);
        using var extractor = new AssemblyStringExtractor();
        return extractor.ExtractAnnotations(path);
    }

    // Compiles the fixture beside the ArchPillar reference assemblies (the test output directory), so the
    // extractor's resolver can load the types it reads the twin attributes from.
    private string Compile(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ILocalizer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "AnnotationFixture_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(AppContext.BaseDirectory, compilation.AssemblyName + ".dll");
        EmitResult result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        _emitted.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _emitted)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup: a still-mapped fixture is harmless test residue under the output directory.
            }
        }
    }
}
