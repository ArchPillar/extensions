using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchPillar.Extensions.Localization.Detection.Tests;

public sealed class LocalizedBundleRegistrationGeneratorTests
{
    // Minimal stand-ins for the runtime types the detector keys off, so the compilation is self-contained and
    // the dependency-injection guard is driven purely by whether IServiceCollection is present in the source.
    private const string Stubs = """
        namespace ArchPillar.Extensions.Localization
        {
            public interface ILocalizer<T> { }

            public abstract class Localized<TSelf> where TSelf : Localized<TSelf>
            {
                protected Localized() { }

                protected Localized(ILocalizer<TSelf> localizer) { }
            }
        }
        """;

    private const string DependencyInjection = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
        }
        """;

    private const string InjectableBundle = """
        namespace Acme
        {
            public sealed class TodoStrings : ArchPillar.Extensions.Localization.Localized<TodoStrings>
            {
                public TodoStrings(ArchPillar.Extensions.Localization.ILocalizer<TodoStrings> localizer) : base(localizer) { }
            }
        }
        """;

    [Fact]
    public void Generator_EmitsRegistration_ForInjectableBundle()
    {
        var registration = Generated(Stubs + DependencyInjection + InjectableBundle, "LocalizedBundleRegistration.g.cs");

        Assert.Contains("internal static class ArchPillarLocalizedBundleRegistration", registration);
        Assert.Contains("AddArchPillarLocalizedBundles(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", registration);
        Assert.Contains("new global::Acme.TodoStrings(", registration);
        Assert.Contains("GetRequiredService<global::ArchPillar.Extensions.Localization.ILocalizer<global::Acme.TodoStrings>>", registration);
    }

    [Fact]
    public void Generator_SkipsBundle_WithOnlyAmbientConstructor()
    {
        // No ILocalizer<TSelf> constructor — ambient-only, cannot be injected, so nothing is emitted.
        const string AmbientOnly = """
            namespace Acme
            {
                public sealed class TodoStrings : ArchPillar.Extensions.Localization.Localized<TodoStrings> { }
            }
            """;

        Assert.False(Produced(Stubs + DependencyInjection + AmbientOnly, "LocalizedBundleRegistration.g.cs"));
    }

    [Fact]
    public void Generator_EmitsNothing_WhenDependencyInjectionNotReferenced()
    {
        Assert.False(Produced(Stubs + InjectableBundle, "LocalizedBundleRegistration.g.cs"));
    }

    [Fact]
    public void Generator_RegistersMultipleBundles_SortedByName()
    {
        const string Bundles = """
            namespace Acme
            {
                public sealed class Beta : ArchPillar.Extensions.Localization.Localized<Beta>
                {
                    public Beta(ArchPillar.Extensions.Localization.ILocalizer<Beta> localizer) : base(localizer) { }
                }

                public sealed class Alpha : ArchPillar.Extensions.Localization.Localized<Alpha>
                {
                    public Alpha(ArchPillar.Extensions.Localization.ILocalizer<Alpha> localizer) : base(localizer) { }
                }
            }
            """;

        var registration = Generated(Stubs + DependencyInjection + Bundles, "LocalizedBundleRegistration.g.cs");

        Assert.Contains("global::Acme.Alpha", registration);
        Assert.Contains("global::Acme.Beta", registration);
        Assert.True(
            registration.IndexOf("Acme.Alpha", StringComparison.Ordinal) < registration.IndexOf("Acme.Beta", StringComparison.Ordinal),
            "Bundles must be emitted in a stable, sorted order.");
    }

    [Fact]
    public void GeneratedRegistration_CompilesAgainstRealDependencyInjection()
    {
        // The stub-based tests assert the emitted text; this one proves it actually binds — the AddSingleton
        // overload, the factory type inference, and GetRequiredService — against the real DI and Localized<T>.
        const string Bundle = """
            using ArchPillar.Extensions.Localization;

            namespace Acme
            {
                public sealed class TodoStrings(ILocalizer<TodoStrings> localizer) : Localized<TodoStrings>(localizer)
                {
                    public string Title => Translate("My Tasks");
                }
            }
            """;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(Bundle);
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        List<MetadataReference> references =
        [
            .. trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => path.Length > 0)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Localized<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location)
        ];

        var compilation = CSharpCompilation.Create(
            "RealReferenceProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        var produced = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Any(generated => generated.HintName == "LocalizedBundleRegistration.g.cs");
        Diagnostic[] errors = [.. updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        Assert.True(produced, "The bundle registration should be generated when DI is referenced.");
        Assert.Empty(errors);
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "BundleProbe",
            [tree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static string Generated(string source, string hintName) =>
        Run(source).Results
            .SelectMany(result => result.GeneratedSources)
            .Single(generated => generated.HintName == hintName)
            .SourceText
            .ToString();

    private static bool Produced(string source, string hintName) =>
        Run(source).Results
            .SelectMany(result => result.GeneratedSources)
            .Any(generated => generated.HintName == hintName);
}
