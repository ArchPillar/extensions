using ArchPillar.Extensions.Localization;

namespace Acme.Greeting;

/// <summary>
/// Demonstrates a <b>localized exception message</b>: the text resolves from the ambient store with no
/// services required — exactly the case that rules out DI (it works in a static constructor or before any
/// container is built). It defaults to the ambient and still accepts an injected localizer.
/// </summary>
public sealed class Validator(ILocalizer<Validator>? localizer = null)
{
    private readonly ILocalizer<Validator> _localizer = localizer ?? Localizer.For<Validator>();

    public void ValidateRequired(string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException(_localizer.Translate("required", "{field} is required.", ("field", field)));
        }
    }
}
