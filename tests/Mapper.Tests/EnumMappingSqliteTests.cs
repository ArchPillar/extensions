using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Verifies that enum mapping expressions are translatable by a real SQL
/// provider (SQLite). The in-memory provider compiles expressions to
/// delegates and cannot detect untranslatable expression trees.
/// </summary>
public sealed class EnumMappingSqliteTests : IDisposable
{
    private readonly SqliteTestDbContext _db;
    private readonly TestMappers _mappers = new();

    public EnumMappingSqliteTests()
    {
        DbContextOptions<SqliteTestDbContext> options = new DbContextOptionsBuilder<SqliteTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new SqliteTestDbContext(options);
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
    // Standalone enum expression — EF Core must translate the conditional chain
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public async Task Project_StandaloneEnumMapper_TranslatesViaSqliteAsync(
        OrderStatus input, OrderStatusDto expected)
    {
        Order seeded = await _db.Orders.SingleAsync(o => o.Status == input);
        var expr = _mappers.OrderStatusMapper.ToExpression();

        OrderStatusDto result = await _db.Orders
            .Where(o => o.Id == seeded.Id)
            .Select(o => o.Status)
            .Select(expr)
            .SingleAsync();

        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // Enum inlined in parent mapper projection — full Order → OrderDto
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public async Task Project_EnumInlinedInParentMapper_TranslatesViaSqliteAsync(
        OrderStatus input, OrderStatusDto expected)
    {
        OrderDto result = await _db.Orders
            .Where(o => o.Status == input)
            .Project(_mappers.Order)
            .SingleAsync();

        Assert.Equal(expected, result.Status);
    }

    // -----------------------------------------------------------------------
    // Large enum (11 values) — verifies the switch expression translates
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PropertyType.Invalid, PropertyTypeDto.Other)]
    [InlineData(PropertyType.Other, PropertyTypeDto.Other)]
    [InlineData(PropertyType.House, PropertyTypeDto.House)]
    [InlineData(PropertyType.RowHouse, PropertyTypeDto.RowHouse)]
    [InlineData(PropertyType.Apartment, PropertyTypeDto.Apartment)]
    [InlineData(PropertyType.Recreational, PropertyTypeDto.Recreational)]
    [InlineData(PropertyType.Cooperative, PropertyTypeDto.Cooperative)]
    [InlineData(PropertyType.Farm, PropertyTypeDto.Farm)]
    [InlineData(PropertyType.LandLeisure, PropertyTypeDto.LandLeisure)]
    [InlineData(PropertyType.LandResidence, PropertyTypeDto.LandResidence)]
    [InlineData(PropertyType.HouseApartment, PropertyTypeDto.HouseApartment)]
    public async Task Project_LargeEnumMapper_TranslatesViaSqliteAsync(
        PropertyType input, PropertyTypeDto expected)
    {
        RealEstateProperty seeded = await _db.Properties.SingleAsync(p => p.Type == input);
        var expr = _mappers.PropertyTypeMapper.ToExpression();

        PropertyTypeDto result = await _db.Properties
            .Where(p => p.Id == seeded.Id)
            .Select(p => p.Type)
            .Select(expr)
            .SingleAsync();

        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // Enum array — client-side mapping of a primitive collection.
    // SQLite does not support SQL APPLY, so server-side projection over
    // primitive collections with Select() is not translatable.  The test
    // fetches the raw entities and maps them in-memory to validate the
    // expression tree is correct.  See EnumMappingPostgresTests for the
    // full server-side translation test.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Map_EnumArray_MapsCorrectlyFromSqliteAsync()
    {
        List<PropertyListing> listings = await _db.Listings
            .OrderBy(l => l.Id)
            .ToListAsync();

        var results = listings
            .Select(l => _mappers.PropertyListing.Map(l)!)
            .ToList();

        Assert.Equal(2, results.Count);

        // Listing 1: House, Apartment, Farm
        Assert.Equal(
            new[] { PropertyTypeDto.House, PropertyTypeDto.Apartment, PropertyTypeDto.Farm },
            results[0].Types);

        // Listing 2: Invalid and Other both map to PropertyTypeDto.Other
        Assert.Equal(
            new[] { PropertyTypeDto.Other, PropertyTypeDto.Other, PropertyTypeDto.Cooperative },
            results[1].Types);
    }

    // -----------------------------------------------------------------------
    // Seed helpers
    // -----------------------------------------------------------------------

    private static void SeedData(SqliteTestDbContext db)
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
                    new OrderLine { Id = 1, ProductName = "Widget", Quantity = 2, UnitPrice = 9.99m },
                    new OrderLine { Id = 2, ProductName = "Gadget", Quantity = 1, UnitPrice = 49.99m },
                ],
            },
            new Order
            {
                Id = 2,
                CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Shipped,
                OwnerId = 20,
                Customer = new Customer { Name = "Bob", Email = "bob@example.com" },
                Lines = [],
            },
            new Order
            {
                Id = 3,
                CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Cancelled,
                OwnerId = 30,
                Customer = new Customer { Name = "Carol", Email = "carol@example.com" },
                Lines = [],
            });

        // Seed one row per PropertyType value (Invalid and Other both map to
        // PropertyTypeDto.Other — use distinct IDs so SingleAsync works).
        PropertyType[] allTypes = Enum.GetValues<PropertyType>();
        for (var i = 0; i < allTypes.Length; i++)
        {
            db.Properties.Add(new RealEstateProperty
            {
                Id    = 100 + i,
                Label = allTypes[i].ToString(),
                Type  = allTypes[i],
            });
        }

        db.Listings.AddRange(
            new PropertyListing
            {
                Id    = 200,
                Name  = "Mixed residential",
                Types = [PropertyType.House, PropertyType.Apartment, PropertyType.Farm],
            },
            new PropertyListing
            {
                Id    = 201,
                Name  = "Edge cases",
                Types = [PropertyType.Invalid, PropertyType.Other, PropertyType.Cooperative],
            });

        db.SaveChanges();
    }
}

// ---------------------------------------------------------------------------
// Minimal DbContext for SQLite-based enum translation tests
// ---------------------------------------------------------------------------

internal sealed class SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<RealEstateProperty> Properties => Set<RealEstateProperty>();
    public DbSet<PropertyListing> Listings => Set<PropertyListing>();

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
        modelBuilder.Entity<RealEstateProperty>(e => e.HasKey(p => p.Id));
        modelBuilder.Entity<PropertyListing>(e => e.HasKey(l => l.Id));
    }
}
