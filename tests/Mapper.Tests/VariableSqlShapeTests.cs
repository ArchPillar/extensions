using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that a <see cref="Variable{T}"/> referenced in a projection is
/// emitted as a SQL parameter — not as an inline literal — when the
/// projection is executed against a real relational provider (SQLite).
/// Parameterization preserves query-plan reuse and avoids any
/// value-dependent SQL text variation.
/// </summary>
public sealed class VariableSqlShapeTests : IDisposable
{
    private readonly VariableSqlShapeDbContext _db;
    private readonly TestMappers _mappers = new();

    public VariableSqlShapeTests()
    {
        DbContextOptions<VariableSqlShapeDbContext> options = new DbContextOptionsBuilder<VariableSqlShapeDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new VariableSqlShapeDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _db.Orders.AddRange(
            new Order
            {
                Id = 1,
                Status = OrderStatus.Pending,
                OwnerId = 42,
                Customer = new Customer { Name = "Alice", Email = "a@b.com" },
                Lines = [],
            },
            new Order
            {
                Id = 2,
                Status = OrderStatus.Shipped,
                OwnerId = 99,
                Customer = new Customer { Name = "Bob", Email = "b@b.com" },
                Lines = [],
            });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public void Project_WithVariableSet_EmitsParameterNotLiteral()
    {
        IQueryable<OrderDto> query = _db.Orders
            .Project(_mappers.Order, o => o.Set(_mappers.CurrentUserId, 42));

        var sql = query.ToQueryString();

        // The SQL must reference a parameter (any placeholder starting with '@')
        // rather than baking the variable's value (42) into the query text.
        Assert.Contains("@", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" = 42", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_WithVariableSet_FiltersByOwnerAsync()
    {
        List<OrderDto> results = await _db.Orders
            .Project(_mappers.Order, o => o.Set(_mappers.CurrentUserId, 42))
            .ToListAsync();

        Assert.True(results.Single(r => r.Id == 1).IsOwner);
        Assert.False(results.Single(r => r.Id == 2).IsOwner);
    }
}

internal sealed class VariableSqlShapeDbContext(DbContextOptions<VariableSqlShapeDbContext> options)
    : DbContext(options)
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
        modelBuilder.Entity<Customer>(e => e.HasKey(c => c.Name));
    }
}
