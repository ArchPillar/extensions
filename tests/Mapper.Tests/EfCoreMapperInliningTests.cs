using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that direct <c>Mapper&lt;,&gt;.Map()</c> and <c>Project()</c> calls
/// used inside hand-written LINQ query projections are inlined into the mapper's
/// projection expression — and therefore translated server-side — when
/// <see cref="MapperDbContextOptionsExtensions.UseArchPillarMapper"/> is
/// registered. Uses SQLite so the full relational translation pipeline runs.
/// </summary>
public sealed class EfCoreMapperInliningTests : IDisposable
{
    private readonly MapperInliningDbContext _db;
    private readonly TestMappers _mappers = new();

    public EfCoreMapperInliningTests()
    {
        DbContextOptionsBuilder<MapperInliningDbContext> builder = new DbContextOptionsBuilder<MapperInliningDbContext>()
            .UseSqlite("DataSource=:memory:");
        builder.UseArchPillarMapper(_mappers);
        DbContextOptions<MapperInliningDbContext> options = builder.Options;

        _db = new MapperInliningDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        SeedData(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // -----------------------------------------------------------------------
    // Scalar Map() — top-level projection via a regular mapper
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ScalarMap_TopLevel_TranslatesToSqlAsync()
    {
        List<OrderDto> results = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => _mappers.Order.Map(o)!)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(OrderStatusDto.Pending, results[0].Status);
        Assert.Equal(OrderStatusDto.Shipped, results[1].Status);
        Assert.Equal(2, results[0].Lines.Count);
        Assert.Single(results[1].Lines);

        // Optional CustomerName is NOT requested → left null.
        Assert.Null(results[0].CustomerName);
    }

    // -----------------------------------------------------------------------
    // Scalar Map() — mapping a single property inside a custom select
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ScalarMap_SingleProperty_InCustomSelect_TranslatesToSqlAsync()
    {
        List<OrderRowVm> results = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => new OrderRowVm
            {
                OrderId = o.Id,
                CustomerEmail = o.Customer.Email,   // hand-written, not from a mapper
                Order = _mappers.Order.Map(o)!,     // one property produced by the mapper
            })
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("alice@example.com", results[0].CustomerEmail);
        Assert.Equal(OrderStatusDto.Pending, results[0].Order.Status);
    }

    // -----------------------------------------------------------------------
    // Collection Project() — mapping one collection property in a custom select
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectionProject_InCustomSelect_TranslatesToSqlAsync()
    {
        List<OrderReportVm> results = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => new OrderReportVm
            {
                OrderId = o.Id,
                LineCount = o.Lines.Count,
                Lines = o.Lines.Project(_mappers.OrderLine).ToList(),
            })
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0].LineCount);
        Assert.Equal(2, results[0].Lines.Count);
        Assert.Contains(results[0].Lines, l => l.ProductName == "Widget");

        // Optional SupplierName is NOT requested → left null.
        Assert.All(results[0].Lines, l => Assert.Null(l.SupplierName));
    }

    // -----------------------------------------------------------------------
    // Scalar Map() with projection options (Include)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ScalarMap_WithIncludeOption_PopulatesOptionalAsync()
    {
        List<OrderDto> results = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => _mappers.Order.Map(o, c => c.Include(m => m.CustomerName))!)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0].CustomerName);
        Assert.Equal("Bob", results[1].CustomerName);
    }

    // -----------------------------------------------------------------------
    // Collection Project() with projection options (Include)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectionProject_WithIncludeOption_PopulatesOptionalAsync()
    {
        List<OrderReportVm> results = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => new OrderReportVm
            {
                OrderId = o.Id,
                LineCount = o.Lines.Count,
                Lines = o.Lines.Project(_mappers.OrderLine, c => c.Include(m => m.SupplierName)).ToList(),
            })
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results[0].Lines, l => Assert.NotNull(l.SupplierName));
        Assert.Contains(results[0].Lines, l => l.SupplierName == "Acme");
    }

    // -----------------------------------------------------------------------
    // The EF options overload also works in-memory (outside a query)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_WithOptions_OutsideQuery_AppliesIncludeInMemory()
    {
        var order = new Order
        {
            Id = 99,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            OwnerId = 1,
            Customer = new Customer { Name = "Carol", Email = "carol@example.com" },
            Lines = [],
        };

        OrderDto? dto = _mappers.Order.Map(order, c => c.Include(m => m.CustomerName));

        Assert.NotNull(dto);
        Assert.Equal("Carol", dto.CustomerName);
    }

    // -----------------------------------------------------------------------
    // Seed helpers
    // -----------------------------------------------------------------------

    private static void SeedData(MapperInliningDbContext db)
    {
        db.Orders.AddRange(
            new Order
            {
                Id = 1,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Pending,
                OwnerId = 10,
                Customer = new Customer { Name = "Alice", Email = "alice@example.com" },
                Lines =
                [
                    new OrderLine { Id = 1, ProductName = "Widget", Quantity = 2, UnitPrice = 9.99m, SupplierName = "Acme" },
                    new OrderLine { Id = 2, ProductName = "Gadget", Quantity = 1, UnitPrice = 19.99m, SupplierName = "Globex" },
                ],
            },
            new Order
            {
                Id = 2,
                CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Shipped,
                OwnerId = 20,
                Customer = new Customer { Name = "Bob", Email = "bob@example.com" },
                Lines =
                [
                    new OrderLine { Id = 3, ProductName = "Sprocket", Quantity = 5, UnitPrice = 4.5m, SupplierName = "Initech" },
                ],
            });

        db.SaveChanges();
    }
}

// ---------------------------------------------------------------------------
// View models for hand-written projections
// ---------------------------------------------------------------------------

internal sealed class OrderRowVm
{
    public int OrderId { get; set; }
    public required string CustomerEmail { get; set; }
    public required OrderDto Order { get; set; }
}

internal sealed class OrderReportVm
{
    public int OrderId { get; set; }
    public int LineCount { get; set; }
    public List<OrderLineDto> Lines { get; set; } = [];
}

// ---------------------------------------------------------------------------
// DbContext with UseArchPillarMapper — uses SQLite for relational translation
// ---------------------------------------------------------------------------

internal sealed class MapperInliningDbContext(DbContextOptions<MapperInliningDbContext> options)
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
