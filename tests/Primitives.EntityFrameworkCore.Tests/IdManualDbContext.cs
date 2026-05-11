using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore;

// Used by HasIdConversionTests — no auto-convention, converters applied explicitly.
internal sealed class IdManualDbContext(DbContextOptions<IdManualDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasIdConversion();
            e.Property(u => u.LatestOrderId).IsRequired(false).HasIdConversion();
        });
    }
}
