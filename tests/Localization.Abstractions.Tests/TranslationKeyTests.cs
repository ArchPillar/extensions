namespace ArchPillar.Extensions.Localization.Abstractions.Tests;

public sealed class TranslationKeyTests
{
    [Fact]
    public void Compose_WithoutContext_ReturnsKey()
    {
        Assert.Equal("home.title", TranslationKey.Compose("home.title", context: null));
        Assert.Equal("home.title", TranslationKey.Compose("home.title", context: string.Empty));
    }

    [Fact]
    public void Compose_WithContext_PrependsContextAndSeparator()
    {
        Assert.Equal("menu\u0004home.title", TranslationKey.Compose("home.title", "menu"));
    }

    [Fact]
    public void Compose_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TranslationKey.Compose(null!, "menu"));
    }
}
