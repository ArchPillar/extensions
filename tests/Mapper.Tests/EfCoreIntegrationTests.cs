using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Mapper.Tests;

/// <summary>
/// Verifies that projection expressions are fully translatable by EF Core
/// (no client-side evaluation). Uses an in-memory provider for isolation.
/// </summary>
public class EfCoreIntegrationTests : IDisposable
{
    private readonly TestDbContext _db;
    private readonly TestMappers   _mappers = new();

    public EfCoreIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())   // unique DB per test
            .Options;

        _db = new TestDbContext(options);
        SeedData(_db);
    }

    public void Dispose() => _db.Dispose();

    // -----------------------------------------------------------------------
    // Basic projection translates without client-side evaluation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_BasicProperties_TranslatesToSql()
    {
        var results = await _db.Orders
            .Project(_mappers.Order)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Id == 1);
        Assert.Contains(results, r => r.Id == 2);
    }

    [Fact]
    public async Task Project_EnumMapping_TranslatesToSql()
    {
        var results = await _db.Orders
            .Project(_mappers.Order)
            .ToListAsync();

        var shipped = results.Single(r => r.Id == 2);
        Assert.Equal(OrderStatusDto.Shipped, shipped.Status);
    }

    // -----------------------------------------------------------------------
    // Nested mapper inlined — no N+1, no client-side Select
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_NestedCollection_InlinedExpression_TranslatesToSql()
    {
        var results = await _db.Orders
            .Include(o => o.Lines)   // EF Core eager load — required for in-memory join
            .Project(_mappers.Order)
            .ToListAsync();

        var orderOne = results.Single(r => r.Id == 1);
        Assert.Equal(2, orderOne.Lines.Count);
        Assert.Contains(orderOne.Lines, l => l.ProductName == "Widget");
    }

    // -----------------------------------------------------------------------
    // Optional properties in projection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_WithOptionalIncluded_PopulatesProperty()
    {
        var results = await _db.Orders
            .Include(o => o.Customer)
            .Project(_mappers.Order, o => o.Include(m => m.CustomerName))
            .ToListAsync();

        Assert.All(results, r => Assert.NotNull(r.CustomerName));
        Assert.Contains(results, r => r.CustomerName == "Alice");
    }

    [Fact]
    public async Task Project_WithoutOptional_PropertyIsNull()
    {
        var results = await _db.Orders
            .Project(_mappers.Order)
            .ToListAsync();

        Assert.All(results, r => Assert.Null(r.CustomerName));
    }

    // -----------------------------------------------------------------------
    // Variable substitution in projection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_WithVariable_FiltersByOwner()
    {
        var results = await _db.Orders
            .Project(_mappers.Order, o => o.Set(_mappers.CurrentUserId, 10))
            .ToListAsync();

        // Order 1 has OwnerId = 10, Order 2 has OwnerId = 20
        Assert.True(results.Single(r => r.Id == 1).IsOwner);
        Assert.False(results.Single(r => r.Id == 2).IsOwner);
    }

    // -----------------------------------------------------------------------
    // Seed helpers
    // -----------------------------------------------------------------------

    private static void SeedData(TestDbContext db)
    {
        db.Orders.AddRange(
            new Order
            {
                Id        = 1,
                CreatedAt = new DateTime(2025, 1, 1),
                Status    = OrderStatus.Pending,
                OwnerId   = 10,
                Customer  = new Customer { Name = "Alice", Email = "alice@example.com" },
                Lines     =
                [
                    new OrderLine { Id = 1, ProductName = "Widget", Quantity = 2, UnitPrice = 9.99m  },
                    new OrderLine { Id = 2, ProductName = "Gadget", Quantity = 1, UnitPrice = 49.99m },
                ],
            },
            new Order
            {
                Id        = 2,
                CreatedAt = new DateTime(2025, 3, 15),
                Status    = OrderStatus.Shipped,
                OwnerId   = 20,
                Customer  = new Customer { Name = "Bob", Email = "bob@example.com" },
                Lines     = [],
            });

        db.SaveChanges();
    }
}

// ---------------------------------------------------------------------------
// Minimal DbContext used only in this test file
// ---------------------------------------------------------------------------

internal class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
            e.HasOne(o => o.Customer).WithOne().HasForeignKey<Customer>("OrderId");
        });

        modelBuilder.Entity<OrderLine>(e => e.HasKey(l => l.Id));
        modelBuilder.Entity<Customer>(e => e.HasKey(c => c.Name));   // Name as PK for simplicity
    }
}
