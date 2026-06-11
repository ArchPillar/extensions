namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// The DI bridge for <see cref="ILocalizer{T}"/>: a registrable concrete type that forwards to the registered
/// <see cref="LocalizationContext"/>'s <see cref="LocalizationContext.For{T}"/>, so an injected
/// <c>ILocalizer&lt;T&gt;</c> reads the same context as the injected <see cref="ILocalizer"/> — whether that
/// context is the ambient one or a fresh container-owned one.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
/// <param name="context">The localization context to resolve against (injected).</param>
internal sealed class AmbientLocalizer<T>(LocalizationContext context) : ILocalizer<T>
{
    private readonly ILocalizer<T> _inner = context.For<T>();

    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments) =>
        _inner.Translate(key, defaultMessage, arguments);

    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments) =>
        _inner.Translate(key, defaultMessage, context, arguments);
}
