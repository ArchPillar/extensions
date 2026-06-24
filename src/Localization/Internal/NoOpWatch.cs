namespace ArchPillar.Extensions.Localization.Internal;

// The watch handle a provider returns when its catalogs never change (or it has nothing to watch).
// Disposing it does nothing.
internal sealed class NoOpWatch : IDisposable
{
    public static readonly NoOpWatch Instance = new();

    private NoOpWatch()
    {
    }

    public void Dispose()
    {
    }
}
