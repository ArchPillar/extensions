namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// The DI bridge for <see cref="ILocalizer{T}"/>: a registrable concrete type (needed because an open-generic
/// registration requires one) that forwards to the registered <see cref="LocalizationContext"/>'s
/// <see cref="LocalizationContext.For{T}"/>, so an injected <c>ILocalizer&lt;T&gt;</c> reads the same context as
/// the injected <see cref="ILocalizer"/> and <see cref="ILocalizerFactory"/>.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
/// <param name="context">The localization context to resolve against (injected).</param>
internal sealed class InjectedLocalizer<T>(LocalizationContext context) : ILocalizer<T>
{
    private readonly ILocalizer<T> _inner = context.For<T>();

    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments) =>
        _inner.Translate(key, defaultMessage, arguments);
}
