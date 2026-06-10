namespace ArchPillar.Extensions.Localization.StringLocalizer;

/// <summary>
/// A registration sentinel that makes <c>AddArchPillarStringLocalizer</c> idempotent per service collection:
/// its presence signals the interop adapters are already registered, so a second call does not stack a second
/// composing factory over the first.
/// </summary>
internal sealed class StringLocalizerMarker;
