using System.Reflection;

namespace ArchPillar.Extensions.Localization.Abstractions.Tests;

public sealed class CatalogModelTests
{
    [Fact]
    public void CatalogEntry_Defaults_AreNeedsTranslationAndEmptyCollections()
    {
        var entry = new CatalogEntry
        {
            Key = "home.title",
            SourceMessage = "Home",
            SourceFingerprint = "abc123"
        };

        Assert.Equal(TranslationState.NeedsTranslation, entry.State);
        Assert.Empty(entry.References);
        Assert.Empty(entry.Placeholders);
        Assert.Null(entry.TranslatedMessage);
    }

    [Fact]
    public void Catalog_Headers_DefaultToEmpty()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries = []
        };

        Assert.Empty(catalog.Entries);
        Assert.Empty(catalog.Headers);
    }

    [Theory]
    [InlineData(typeof(TranslatableAttribute), AttributeTargets.Parameter)]
    [InlineData(typeof(TranslationDefaultAttribute), AttributeTargets.Parameter)]
    [InlineData(typeof(TranslationContextAttribute), AttributeTargets.Parameter)]
    [InlineData(typeof(TranslationCommentAttribute), AttributeTargets.Parameter | AttributeTargets.Method)]
    public void Attributes_TargetExpectedSymbols(Type attributeType, AttributeTargets expectedTargets)
    {
        AttributeUsageAttribute? usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(expectedTargets, usage!.ValidOn);
    }

    [Fact]
    public void Capabilities_CombineAsFlags()
    {
        const FormatCapabilities Combined = FormatCapabilities.Context | FormatCapabilities.IcuPlural;

        Assert.True(Combined.HasFlag(FormatCapabilities.Context));
        Assert.True(Combined.HasFlag(FormatCapabilities.IcuPlural));
        Assert.False(Combined.HasFlag(FormatCapabilities.NativePlural));
    }
}
