// SKILL-GENERATED (archpillar-localization). Scenario: everyday surface — ILocalizer<T>
// category scope, a Localized<T> partial bundle via DI, and ambient ICU plural. No catalogs
// present, so every lookup renders the in-code default.
// Skill-oracle: everyday surface of ArchPillar.Extensions.Localization.
// Three entry points: ILocalizer<T> (category-scoped), Localized<TSelf> (bundle),
// and the receiver-less ambient Localizer.Translate. No catalogs present, so every
// lookup falls through to the in-code ICU default.
using Microsoft.Extensions.DependencyInjection;
using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.DependencyInjection;
using static ArchPillar.Extensions.Localization.Localizer;

namespace LocOracle;

// Category-scoped consumer (the ILogger<T> model): keys live under this type's category.
public sealed class Checkout(ILocalizer<Checkout> localizer)
{
    public string Pay => localizer.Translate("pay", "Pay now");

    public string Total(decimal amount) =>
        localizer.Translate("total", "Total due: {amount}", ("amount", amount));
}

// A bundle of fixed labels: member name = key, deriving type = category.
// 'partial' lets the DI constructor be generated; registered via AddArchPillarLocalizedBundles().
public sealed partial class ButtonLabels : Localized<ButtonLabels>
{
    public string Save => Translate("Save");

    public string Cancel => Translate("Cancel");
}

public static class Demo
{
    public static void Run()
    {
        var services = new ServiceCollection();
        services
            .AddArchPillarLocalization(new LocalizerOptions
            {
                SourceCulture = "en",
                TranslationsDirectory = "Translations",
            })
            .AddArchPillarLocalizedBundles();
        services.AddScoped<Checkout>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var checkout = scope.ServiceProvider.GetRequiredService<Checkout>();
        var labels = scope.ServiceProvider.GetRequiredService<ButtonLabels>();

        Console.WriteLine("== Demo: defaults render with no catalogs ==");
        Console.WriteLine("  checkout.Pay   : " + checkout.Pay);
        Console.WriteLine("  checkout.Total : " + checkout.Total(42.50m));
        Console.WriteLine("  labels.Save    : " + labels.Save);
        Console.WriteLine("  labels.Cancel  : " + labels.Cancel);

        // Receiver-less ambient Translate with an ICU plural (same store as the injected views).
        Console.WriteLine("  ambient/1      : " + Translate(
            "cart",
            "{count, plural, =0 {Your cart is empty} one {# item in cart} other {# items in cart}}",
            ("count", 1)));
        Console.WriteLine("  ambient/3      : " + Translate(
            "cart",
            "{count, plural, =0 {Your cart is empty} one {# item in cart} other {# items in cart}}",
            ("count", 3)));
    }
}
