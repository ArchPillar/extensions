using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that <c>EnumMapper&lt;,&gt;.Map()</c> calls used directly in LINQ
/// queries are translatable when <see cref="MapperDbContextOptionsExtensions.UseArchPillarMapper"/>
/// is registered.  Uses SQLite (a relational provider) so the full SQL
/// translation pipeline — including the custom <c>IMethodCallTranslator</c> —
/// is exercised.
/// </summary>
public sealed class EfCoreEnumTranslationTests : IDisposable
{
    private readonly EnumTranslationDbContext _db;
    private readonly TestMappers _mappers = new();

    public EfCoreEnumTranslationTests()
    {
        DbContextOptionsBuilder<EnumTranslationDbContext> builder = new DbContextOptionsBuilder<EnumTranslationDbContext>()
            .UseSqlite("DataSource=:memory:");
        builder.UseArchPillarMapper(_mappers);
        DbContextOptions<EnumTranslationDbContext> options = builder.Options;

        _db = new EnumTranslationDbContext(options);
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
    // Direct Map() call in Select — small enum (3 values)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DirectMap_SmallEnum_TranslatesToSqlAsync()
    {
        List<OrderStatusDto> results = await _db.Orders
            .Select(o => _mappers.OrderStatusMapper.Map(o.Status))
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(OrderStatusDto.Pending, results);
        Assert.Contains(OrderStatusDto.Shipped, results);
    }

    // -----------------------------------------------------------------------
    // Direct Map() call in Select — large enum (11 values)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PropertyType.Invalid, PropertyTypeDto.Other)]
    [InlineData(PropertyType.House, PropertyTypeDto.House)]
    [InlineData(PropertyType.HouseApartment, PropertyTypeDto.HouseApartment)]
    public async Task DirectMap_LargeEnum_TranslatesToSqlAsync(
        PropertyType seeded, PropertyTypeDto expected)
    {
        List<RealEstatePropertyDto> results = await _db.Properties
            .Where(p => p.Type == seeded)
            .Select(p => new RealEstatePropertyDto
            {
                Id = p.Id,
                Label = p.Label,
                Type = _mappers.PropertyTypeMapper.Map(p.Type),
            })
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal(expected, results[0].Type);
    }

    // -----------------------------------------------------------------------
    // Direct nullable Map() call — Map(TSource?) → TDest?
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DirectMap_NullableToNullable_NonNull_TranslatesToSqlAsync()
    {
        List<OrderStatusDto?> results = await _db.NullableOrders
            .Where(o => o.Status != null)
            .Select(o => _mappers.OrderStatusMapper.Map(o.Status))
            .ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Contains(OrderStatusDto.Pending, results);
        Assert.Contains(OrderStatusDto.Shipped, results);
        Assert.Contains(OrderStatusDto.Cancelled, results);
    }

    [Fact]
    public async Task DirectMap_NullableToNullable_Null_TranslatesToSqlAsync()
    {
        List<OrderStatusDto?> results = await _db.NullableOrders
            .Where(o => o.Status == null)
            .Select(o => _mappers.OrderStatusMapper.Map(o.Status))
            .ToListAsync();

        Assert.Single(results);
        Assert.Null(results[0]);
    }

    // -----------------------------------------------------------------------
    // Direct nullable Map() call with default — Map(TSource?, TDest) → TDest
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DirectMap_NullableWithDefault_NonNull_TranslatesToSqlAsync()
    {
        List<OrderStatusDto> results = await _db.NullableOrders
            .Where(o => o.Status != null)
            .Select(o => _mappers.OrderStatusMapper.Map(o.Status, OrderStatusDto.Pending))
            .ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Contains(OrderStatusDto.Pending, results);
        Assert.Contains(OrderStatusDto.Shipped, results);
        Assert.Contains(OrderStatusDto.Cancelled, results);
    }

    [Fact]
    public async Task DirectMap_NullableWithDefault_Null_TranslatesToSqlAsync()
    {
        List<OrderStatusDto> results = await _db.NullableOrders
            .Where(o => o.Status == null)
            .Select(o => _mappers.OrderStatusMapper.Map(o.Status, OrderStatusDto.Shipped))
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal(OrderStatusDto.Shipped, results[0]);
    }

    // -----------------------------------------------------------------------
    // Project() still works alongside UseArchPillarMapper
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_WithUseArchPillarMapper_StillWorksAsync()
    {
        List<OrderDto> results = await _db.Orders
            .Project(_mappers.Order)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Status == OrderStatusDto.Pending);
        Assert.Contains(results, r => r.Status == OrderStatusDto.Shipped);
    }

    // -----------------------------------------------------------------------
    // Seed helpers
    // -----------------------------------------------------------------------

    private static void SeedData(EnumTranslationDbContext db)
    {
        db.Orders.AddRange(
            new Order
            {
                Id = 1,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Pending,
                OwnerId = 10,
                Customer = new Customer { Name = "Alice", Email = "alice@example.com" },
                Lines = [],
            },
            new Order
            {
                Id = 2,
                CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Shipped,
                OwnerId = 20,
                Customer = new Customer { Name = "Bob", Email = "bob@example.com" },
                Lines = [],
            });

        // Seed one property per type to verify the large-enum translation.
        var id = 1;

        foreach (PropertyType type in Enum.GetValues<PropertyType>())
        {
            db.Properties.Add(new RealEstateProperty
            {
                Id = id++,
                Label = type.ToString(),
                Type = type,
            });
        }

        db.NullableOrders.AddRange(
            new OrderWithNullableStatus { Id = 200, Status = OrderStatus.Pending },
            new OrderWithNullableStatus { Id = 201, Status = OrderStatus.Shipped },
            new OrderWithNullableStatus { Id = 202, Status = OrderStatus.Cancelled },
            new OrderWithNullableStatus { Id = 203, Status = null });

        db.SaveChanges();
    }
}

// ---------------------------------------------------------------------------
// DbContext with UseArchPillarMapper — uses SQLite for relational translation
// ---------------------------------------------------------------------------

internal sealed class EnumTranslationDbContext(DbContextOptions<EnumTranslationDbContext> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<RealEstateProperty> Properties => Set<RealEstateProperty>();
    public DbSet<OrderWithNullableStatus> NullableOrders => Set<OrderWithNullableStatus>();

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

        modelBuilder.Entity<RealEstateProperty>(e =>
        {
            e.HasKey(p => p.Id);
        });

        modelBuilder.Entity<OrderWithNullableStatus>(e => e.HasKey(o => o.Id));
    }
}
