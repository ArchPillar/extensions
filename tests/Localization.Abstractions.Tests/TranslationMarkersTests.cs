using System.Reflection;

namespace ArchPillar.Extensions.Localization.Abstractions.Tests;

public sealed class TranslationMarkersTests
{
    [Fact]
    public void L_ReturnsTheTextUnchanged() =>
        Assert.Equal("Email is required", TranslationMarkers.L("Email is required"));

    [Fact]
    public void L_ParameterCarriesTranslatableAndDefaultAttributes()
    {
        ParameterInfo parameter = typeof(TranslationMarkers)
            .GetMethod(nameof(TranslationMarkers.L))!
            .GetParameters()[0];

        Assert.NotNull(parameter.GetCustomAttribute<TranslatableAttribute>());
        Assert.NotNull(parameter.GetCustomAttribute<TranslationDefaultAttribute>());
    }
}
