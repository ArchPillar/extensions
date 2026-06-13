using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Models.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primitives.TypedIdsSample.Catalog;

namespace Primitives.TypedIdsSample.Data;

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The User/Order keys and Order.OwnerId FK are left to ride the
        // UseArchPillarTypedIds() auto-convention — every Id<T> property gets
        // its Guid converter without a line of config.
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);

            // The nullable LatestOrderId is configured explicitly to show the
            // per-property path: HasIdConversion<OrderTag>() applies the same
            // Id<OrderTag> ↔ Guid converter by hand.
            e.Property(u => u.LatestOrderId).HasIdConversion<Id<OrderTag>>();
        });

        modelBuilder.Entity<Order>(e => e.HasKey(o => o.Id));
    }
}
