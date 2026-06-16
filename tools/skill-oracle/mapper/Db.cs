using Microsoft.EntityFrameworkCore;
namespace MapTest;

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public AppDbContext(DbContextOptions<AppDbContext> o) : base(o) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Order>().OwnsOne(o => o.Customer);
        mb.Entity<Order>().OwnsMany(o => o.Lines, b => b.OwnsOne(l => l.Product));
    }
}
