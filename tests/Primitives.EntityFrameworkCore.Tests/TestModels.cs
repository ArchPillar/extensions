using ArchPillar.Extensions.Identifiers;

namespace ArchPillar.Extensions.Primitives.EntityFrameworkCore.Tests;

// Phantom type markers — not real entities, just compile-time tags.
internal sealed class UserTag;

internal sealed class OrderTag;

internal sealed class UserEntity
{
    public Id<UserTag> Id { get; set; }

    public string Name { get; set; } = "";

    public Id<OrderTag>? LatestOrderId { get; set; }
}

internal sealed class OrderEntity
{
    public Id<OrderTag> Id { get; set; }

    public string Title { get; set; } = "";

    public Id<UserTag> OwnerId { get; set; }
}
