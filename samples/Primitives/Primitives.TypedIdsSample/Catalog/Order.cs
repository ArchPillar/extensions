using ArchPillar.Extensions.Models;

namespace Primitives.TypedIdsSample.Catalog;

internal sealed class Order
{
    public Id<OrderTag> Id { get; set; }

    public string Title { get; set; } = "";

    public Id<UserTag> OwnerId { get; set; }
}
