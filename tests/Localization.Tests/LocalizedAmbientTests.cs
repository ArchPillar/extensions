namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The ambient (parameterless) construction path of <see cref="Localized{TSelf}"/>: <c>new Bundle()</c> binds
/// to the process-wide ambient context with no services and no registration, and fails fast when that context
/// has been disabled. Runs in the serial Ambient collection because it touches the static facade.
/// </summary>
[Collection("Ambient")]
public sealed class LocalizedAmbientTests : IDisposable
{
    public LocalizedAmbientTests()
    {
        Localizer.ResetAmbientForTests();
    }

    public void Dispose() => Localizer.ResetAmbientForTests();

    [Fact]
    public void Parameterless_ResolvesFromAmbient_FallsBackToInCodeDefault()
    {
        var buttons = new AmbientButtons();

        Assert.Equal("Save", buttons.Save);
    }

    [Fact]
    public void Parameterless_WhenAmbientDisabled_Throws()
    {
        Localizer.Disable();

        Assert.Throws<InvalidOperationException>(() => new AmbientButtons());
    }

    private sealed class AmbientButtons : Localized<AmbientButtons>
    {
        public string Save => Translate("Save");
    }
}
