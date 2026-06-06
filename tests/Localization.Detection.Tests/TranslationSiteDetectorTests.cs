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

    private static List<TranslationSiteResult> Detect() =>
        TranslationSiteDetector.Detect(RoslynTestHost.CreateCompilation(Source), CancellationToken.None).ToList();

    private static TranslationSite Single(List<TranslationSiteResult> results, string key)
    {
        TranslationSite? site = results.Select(r => r.Site).FirstOrDefault(s => s?.Key == key);
        Assert.NotNull(site);
        return site!;
    }
}
