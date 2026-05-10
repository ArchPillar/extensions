using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore;

// Used by IdConventionTests — no UseArchPillarTypedIds at all.
internal sealed class IdNoConventionDbContext(DbContextOptions<IdNoConventionDbContext> options)
    : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.LatestOrderId).IsRequired(false);
        });
    }
}
