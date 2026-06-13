using System.Reflection;

namespace ArchPillar.Extensions.Localization.Abstractions.Tests;

public sealed class TranslationScopeAttributeTests
{
    [Fact]
    public void TranslationScope_TargetsGenericParametersOnly()
    {
        AttributeUsageAttribute? usage = typeof(TranslationScopeAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.GenericParameter, usage!.ValidOn);
    }
}
