namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// The DI bridge for <see cref="ILocalizer{T}"/>: a registrable concrete type that forwards to the ambient
/// <see cref="Localization.For{T}"/>, so an injected <c>ILocalizer&lt;T&gt;</c> reads the same store as an
/// exception text or a non-DI caller.
/// </summary>
/// <typeparam name="T">The type whose full name is the translation category.</typeparam>
internal sealed class AmbientLocalizer<T> : ILocalizer<T>
{
    private readonly ILocalizer<T> _inner = Localization.For<T>();

    public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments) =>
        _inner.Translate(key, defaultMessage, arguments);

    public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments) =>
        _inner.Translate(key, defaultMessage, context, arguments);
}
