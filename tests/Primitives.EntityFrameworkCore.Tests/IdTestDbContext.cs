using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

internal sealed class IdTestDbContext(DbContextOptions<IdTestDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.LatestOrderId).IsRequired(false);
        });

        modelBuilder.Entity<OrderEntity>(e => e.HasKey(o => o.Id));
    }
}
