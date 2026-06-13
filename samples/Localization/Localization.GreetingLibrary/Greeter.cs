using ArchPillar.Extensions.Localization;

namespace Acme.Greeting;

/// <summary>
/// A library service with translatable strings. It defaults to the ambient store — so a no-DI app can just
/// <c>new Greeter()</c> and the library's translations work — while still accepting an injected
/// <see cref="ILocalizer{T}"/> for hosts that use DI. The English default ships in code; the German override
/// ships embedded in this library's own DLL.
/// </summary>
public sealed class Greeter(ILocalizer<Greeter>? localizer = null)
{
    private readonly ILocalizer<Greeter> _localizer = localizer ?? Localizer.For<Greeter>();

    public string Greet(string name) =>
        _localizer.Translate("greeting", "Hello {name}!", ("name", name));
}
