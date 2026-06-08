using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Models.EntityFrameworkCore;
using ArchPillar.Extensions.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Primitives.TypedIdsSample.Catalog;
using Primitives.TypedIdsSample.Data;

// ---------------------------------------------------------------------------
// Primitives.TypedIdsSample
//
// Demonstrates the opt-in ArchPillar.Extensions.Primitives.EntityFrameworkCore
// add-on over SQLite:
//   - UseArchPillarTypedIds() auto-converts every Id<T> property to its Guid
//     column (the User/Order keys and the Order.OwnerId FK).
//   - One explicit Property(...).HasIdConversion() for the nullable
//     User.LatestOrderId — the per-property path.
//   - Insert + query round-trip with a LINQ lookup by typed id translated to SQL.
//   - Operations return OperationResult (Ok on a hit, NotFound for a missing id).
//
// Domain types live under Catalog/, persistence under Data/ — one file per class.
// ---------------------------------------------------------------------------

// Hold one in-memory SQLite connection open for the whole app lifetime so the
// database survives across DbContext instances.
using var connection = new SqliteConnection("Filename=:memory:");
connection.Open();

DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
    .UseSqlite(connection)
    .UseArchPillarTypedIds()
    .Options;

using (var context = new CatalogDbContext(options))
{
    await context.Database.EnsureCreatedAsync();

    // Seed a deterministic-shaped graph: one user owning one order.
    var userId = Id<UserTag>.New();
    var orderId = Id<OrderTag>.New();

    context.Users.Add(new User { Id = userId, Name = "Ada Lovelace" });
    context.Orders.Add(new Order { Id = orderId, Title = "First Order", OwnerId = userId });
    await context.SaveChangesAsync();

    var store = new CatalogStore(context);

    Console.WriteLine("== round-trip: query user by its typed Id<UserTag> ==");
    OperationResult<User> userResult = await store.GetUserAsync(userId);
    if (userResult.IsSuccess)
    {
        User user = userResult.Value!;
        Console.WriteLine($"  status={userResult.Status} name={user.Name}");
        Console.WriteLine($"  typed id round-trips: {user.Id.Value == userId.Value}");
    }

    Console.WriteLine();
    Console.WriteLine("== query order by OwnerId FK (typed id in LINQ -> SQL) ==");
    OperationResult<Order> orderResult = await store.GetOrderByOwnerAsync(userId);
    if (orderResult.IsSuccess)
    {
        Order order = orderResult.Value!;
        Console.WriteLine($"  status={orderResult.Status} title={order.Title}");
        Console.WriteLine($"  owner matches: {order.OwnerId.Value == userId.Value}");
    }

    Console.WriteLine();
    Console.WriteLine("== not-found: query a freshly-minted id that was never stored ==");
    OperationResult<User> missing = await store.GetUserAsync(Id<UserTag>.New());
    Console.WriteLine($"  status={missing.Status} detail={missing.Problem?.Detail}");

    Console.WriteLine();
    Console.WriteLine("== per-property path: set + read back nullable LatestOrderId ==");
    User stored = (await store.GetUserAsync(userId)).Value!;
    stored.LatestOrderId = orderId;
    await context.SaveChangesAsync();

    // New context proves the value really persisted through the explicit
    // HasIdConversion() converter, not just the in-memory tracked instance.
    using var verifyContext = new CatalogDbContext(options);
    User reloaded = await verifyContext.Users.SingleAsync(u => u.Id == userId);
    Console.WriteLine($"  LatestOrderId has value: {reloaded.LatestOrderId.HasValue}");
    Console.WriteLine($"  matches seeded order: {reloaded.LatestOrderId?.Value == orderId.Value}");
}
