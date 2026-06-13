using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options)
{
    public DbSet<TestRow> Rows => Set<TestRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestRow>(entity =>
        {
            entity.ToTable("test_rows");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uuid");
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            entity.Property(x => x.OccurredAt).HasColumnType("timestamptz");
            entity.Property(x => x.Priority).HasColumnType("integer");
        });
    }
}
