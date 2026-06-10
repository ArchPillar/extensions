namespace ArchPillar.Extensions.Localization.Detection.Tests;

public sealed class TranslationSiteDetectorTests
{
    private const string Source = """
        using ArchPillar.Extensions.Localization;

        public static class T
        {
            public static string Translate(
                [Translatable] string key,
                [TranslationDefault] string message,
                params (string Name, object? Value)[] arguments) => message;
        }

        public class Consumer
        {
            public void Run(string dynamicKey)
            {
                T.Translate("home.title", "Home");
                T.Translate("inbox", "You have {count, plural, one {# message} other {# messages}}", ("count", 1));
                T.Translate(dynamicKey, "Dynamic");
                T.Translate("broken", "{count, plural, one {x}");
            }
        }
        """;

    [Fact]
    public void Detect_ConstantCall_YieldsSiteWithKeyDefaultAndPlaceholders()
    {
        List<TranslationSiteResult> results = Detect();

        TranslationSite site = Single(results, "home.title");
        Assert.Equal("Home", site.DefaultMessage);
        Assert.Empty(site.Placeholders);

        TranslationSite plural = Single(results, "inbox");
        Assert.Equal(["count"], plural.Placeholders);
    }

    [Fact]
    public void Detect_NonConstantKey_ReportsNonConstantProblem()
    {
        List<TranslationSiteResult> results = Detect();

        Assert.Contains(
            results,
            r => r.Site is null && r.Problems.Any(p => p.Cause == DetectionCause.NonConstantArgument));
    }

    [Fact]
    public void Detect_InvalidMessage_ReportsInvalidFormatProblem()
    {
        List<TranslationSiteResult> results = Detect();

        TranslationSiteResult broken = Assert.Single(
            results,
            r => r.Site?.Key == "broken");
        Assert.Contains(broken.Problems, p => p.Cause == DetectionCause.InvalidMessageFormat);
    }

    [Fact]
    public void Detect_WithoutAttributes_YieldsNothing()
    {
        const string Plain = """
            public class C
            {
                public void M() => System.Console.WriteLine("hi");
            }
            """;

        Assert.Empty(TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Plain), CancellationToken.None));
    }

    [Fact]
    public void Detect_GlobalCall_HasEmptyCategory() =>
        Assert.Equal(string.Empty, Single(Detect(), "home.title").Category);

    [Fact]
    public void Detect_ScopedReceiver_TakesCategoryFromTypeArgument()
    {
        const string Scoped = """
            using ArchPillar.Extensions.Localization;

            public interface IScoped<[TranslationScope] T>
            {
                string Translate([Translatable] string key, [TranslationDefault] string message);
            }

            public sealed class Home;

            public sealed class Consumer(IScoped<Home> loc)
            {
                public string Title() => loc.Translate("title", "Inbox");
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Scoped), CancellationToken.None).ToList();

        Assert.Equal("Home", Single(results, "title").Category);
    }

    [Fact]
    public void Detect_ScopedBaseClass_TakesCategoryFromTheDerivedType()
    {
        const string Scoped = """
            using ArchPillar.Extensions.Localization;

            public abstract class Bundle<[TranslationScope] TSelf> where TSelf : Bundle<TSelf>
            {
                protected string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public sealed class Buttons : Bundle<Buttons>
            {
                public string Save() => Translate("save", "Save");
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Scoped), CancellationToken.None).ToList();

        Assert.Equal("Buttons", Single(results, "save").Category);
    }

    [Fact]
    public void Detect_LMarker_HarvestsLiteralAsKeyAndDefaultInGlobalCategory()
    {
        const string Marked = """
            using static ArchPillar.Extensions.Localization.TranslationMarkers;

            public sealed class Consumer
            {
                public string Required() => L("Email is required");
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Marked), CancellationToken.None).ToList();

        TranslationSite site = Single(results, "Email is required");
        Assert.Equal("Email is required", site.DefaultMessage);
        Assert.Equal(string.Empty, site.Category);
    }

    private const string StringLocalizerSource = """
        using Microsoft.Extensions.Localization;

        public sealed class Home;

        public sealed class Consumer(IStringLocalizer<Home> scoped, IStringLocalizer plain)
        {
            public string Required() => scoped["Email is required"];
            public string Dynamic(string key) => scoped[key];
            public string FormatStyle() => scoped["Price: {0:C}"];
            public string Positional() => scoped["Hello {0}", "Ada"];
            public string Shared() => plain["Shared text"];
        }
        """;

    [Fact]
    public void Detect_StringLocalizerConstant_ExtractsLiteralAsKeyDefaultAndCategoryFromTypeArgument()
    {
        List<TranslationSiteResult> results = DetectStringLocalizer();

        TranslationSite site = Single(results, "Email is required");
        Assert.Equal("Email is required", site.DefaultMessage);
        Assert.Equal("Home", site.Category);
        Assert.Empty(site.Placeholders);
    }

    [Fact]
    public void Detect_StringLocalizerNonGeneric_HasGlobalCategory() =>
        Assert.Equal(string.Empty, Single(DetectStringLocalizer(), "Shared text").Category);

    [Fact]
    public void Detect_StringLocalizerPositionalPlaceholder_IsKeptVerbatim()
    {
        TranslationSite site = Single(DetectStringLocalizer(), "Hello {0}");
        Assert.Equal(["0"], site.Placeholders);
    }

    [Fact]
    public void Detect_StringLocalizerDynamicKey_IsSkippedSilently()
    {
        List<TranslationSiteResult> results = DetectStringLocalizer();

        Assert.DoesNotContain(results, r => r.Site is null);
        Assert.DoesNotContain(results, r => r.Problems.Count > 0);
    }

    [Fact]
    public void Detect_StringLocalizerNonIcuLiteral_IsSkippedSilently() =>
        Assert.DoesNotContain(DetectStringLocalizer(), r => r.Site?.Key == "Price: {0:C}");

    [Fact]
    public void Detect_GenericScopeType_UsesTheOpenGenericNameWithArity()
    {
        const string Scoped = """
            using ArchPillar.Extensions.Localization;

            public interface IScoped<[TranslationScope] T>
            {
                string Translate([Translatable] string key, [TranslationDefault] string message);
            }

            public sealed class Box<TItem>;

            public sealed class Consumer(IScoped<Box<int>> loc)
            {
                public string Title() => loc.Translate("title", "Inbox");
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Scoped), CancellationToken.None).ToList();

        Assert.Equal("Box`1", Single(results, "title").Category);
    }

    [Fact]
    public void Detect_ExtensionMethodReceiver_TakesCategoryFromTheScopedThisParameter()
    {
        // A reduced extension-method call has no Instance; the receiver (the `this` parameter) carries the
        // [TranslationScope], so the category must still be resolved from it rather than falling to global.
        const string Scoped = """
            using ArchPillar.Extensions.Localization;

            public interface IScoped<[TranslationScope] T>;

            public sealed class Save;

            public static class ScopedExtensions
            {
                public static string Translate<T>(this IScoped<T> scope, [Translatable] string key, [TranslationDefault] string message) => message;
            }

            public sealed class Consumer(IScoped<Save> scope)
            {
                public string Run() => scope.Translate("label", "Save");
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Scoped), CancellationToken.None).ToList();

        Assert.Equal("Save", Single(results, "label").Category);
    }

    [Fact]
    public void Detect_ObjectCreationReceiver_TakesCategoryFromTheConstructedType()
    {
        // The translation site is the construction itself; the constructed generic type carries the
        // [TranslationScope], so the category comes from its type argument, not global.
        const string Scoped = """
            using ArchPillar.Extensions.Localization;

            public sealed class Buttons;

            public sealed class Widget<[TranslationScope] TScope>
            {
                public Widget([Translatable] string key, [TranslationDefault] string message) { }
            }

            public sealed class Consumer
            {
                public object Run() => new Widget<Buttons>("save", "Save");
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Scoped), CancellationToken.None).ToList();

        Assert.Equal("Buttons", Single(results, "save").Category);
    }

    [Fact]
    public void Detect_Forwarder_PassesItsOwnTranslatableParameterThrough_IsSkipped()
    {
        // A thin wrapper whose [Translatable] parameter is forwarded to an inner translatable call is
        // plumbing: the literal is at the wrapper's own call sites, so the forwarding call is not extracted
        // and is not flagged as a non-constant key.
        const string Forwarder = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public static class MyStrings
            {
                public static string Tr([Translatable] string key, [TranslationDefault] string message) => T.Translate(key, message);
            }
            """;

        var results = TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Forwarder), CancellationToken.None).ToList();

        Assert.DoesNotContain(results, r => r.Site is not null);
        Assert.Empty(results.SelectMany(r => r.Problems));
    }

    private static List<TranslationSiteResult> DetectStringLocalizer() =>
        TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(StringLocalizerSource), CancellationToken.None).ToList();

    private static List<TranslationSiteResult> Detect() =>
        TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Source), CancellationToken.None).ToList();

    private static TranslationSite Single(List<TranslationSiteResult> results, string key)
    {
        TranslationSite? site = results.Select(r => r.Site).FirstOrDefault(s => s?.Key == key);
        Assert.NotNull(site);
        return site!;
    }
}
