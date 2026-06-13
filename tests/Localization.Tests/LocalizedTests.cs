namespace ArchPillar.Extensions.Localization.Tests;

public sealed class LocalizedTests
{
    [Fact]
    public void Translate_UsesCallingMemberNameAsKey()
    {
        var localizer = new RecordingLocalizer<Buttons>();
        var buttons = new Buttons(localizer);

        var rendered = buttons.Save;

        Assert.Equal("Save", localizer.LastKey);
        Assert.Equal("Save", localizer.LastDefault);
        Assert.Equal("Save|Save", rendered);
    }

    [Fact]
    public void Translate_WithArguments_UsesMemberNameAndForwardsArguments()
    {
        var localizer = new RecordingLocalizer<Buttons>();
        var buttons = new Buttons(localizer);

        var rendered = buttons.Greeting("Ada");

        Assert.Equal("Greeting", localizer.LastKey);
        Assert.Equal("Hello {name}", localizer.LastDefault);
        Assert.Equal("Ada", localizer.LastArguments.Single().Value);
        Assert.Equal("Greeting|Hello {name}", rendered);
    }

    private sealed class Buttons(ILocalizer<Buttons> localizer) : Localized<Buttons>(localizer)
    {
        public string Save => Translate("Save");

        public string Greeting(string name)
        {
            return Translate("Hello {name}", [("name", name)]);
        }
    }

    private sealed class RecordingLocalizer<T> : ILocalizer<T>
    {
        public string LastKey { get; private set; } = "";

        public string LastDefault { get; private set; } = "";

        public (string Name, object? Value)[] LastArguments { get; private set; } = [];

        public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments)
        {
            LastKey = key;
            LastDefault = defaultMessage;
            LastArguments = arguments;
            return $"{key}|{defaultMessage}";
        }

        public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments)
        {
            return Translate(key, defaultMessage, arguments);
        }
    }
}
