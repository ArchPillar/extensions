using ArchPillar.Extensions.Models;

namespace Primitives.TypedIdsSample.Catalog;

internal sealed class User
{
    public Id<UserTag> Id { get; set; }

    public string Name { get; set; } = "";

    public Id<OrderTag>? LatestOrderId { get; set; }
}
