using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that enum mapping expressions are translatable by the Npgsql
/// (PostgreSQL) provider. Uses the shared <see cref="PostgresFixture"/>
/// container; each test class gets an isolated database within it.
/// </summary>
[Collection("PostgreSQL")]
public sealed class EnumMappingPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private PostgresTestDbContext _db = null!;
    private readonly TestMappers _mappers = new();

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);

        DbContextOptions<PostgresTestDbContext> options = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        _db = new PostgresTestDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        SeedData(_db);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // -----------------------------------------------------------------------
    // Standalone enum expression — Npgsql must translate the conditional chain
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public async Task Project_StandaloneEnumMapper_TranslatesViaPostgresAsync(
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
    public async Task Project_EnumInlinedInParentMapper_TranslatesViaPostgresAsync(
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
    public async Task Project_LargeEnumMapper_TranslatesViaPostgresAsync(
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
    // Seed helpers
    // -----------------------------------------------------------------------

    private static void SeedData(PostgresTestDbContext db)
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

        db.NullableOrders.AddRange(
            new OrderWithNullableStatus { Id = 200, Status = OrderStatus.Pending },
            new OrderWithNullableStatus { Id = 201, Status = OrderStatus.Shipped },
            new OrderWithNullableStatus { Id = 202, Status = OrderStatus.Cancelled },
            new OrderWithNullableStatus { Id = 203, Status = null });
    }
}

// ---------------------------------------------------------------------------
// Nullable enum PostgreSQL translation tests
// ---------------------------------------------------------------------------

[Collection("PostgreSQL")]
public sealed class NullableEnumMappingPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private PostgresTestDbContext _db = null!;
    private readonly NullableEnumMappers _mappers = new();

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);

        DbContextOptions<PostgresTestDbContext> options = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        _db = new PostgresTestDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.NullableOrders.AddRange(
            new OrderWithNullableStatus { Id = 1, Status = OrderStatus.Pending },
            new OrderWithNullableStatus { Id = 2, Status = OrderStatus.Shipped },
            new OrderWithNullableStatus { Id = 3, Status = OrderStatus.Cancelled },
            new OrderWithNullableStatus { Id = 4, Status = null });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Theory]
    [InlineData(1, OrderStatusDto.Pending)]
    [InlineData(2, OrderStatusDto.Shipped)]
    [InlineData(3, OrderStatusDto.Cancelled)]
    public async Task Project_NullableToNullable_NonNull_TranslatesViaPostgresAsync(
        int id, OrderStatusDto expected)
    {
        OrderDtoWithNullableStatus result = await _db.NullableOrders
            .Where(o => o.Id == id)
            .Project(_mappers.NullableToNullable)
            .SingleAsync();

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Project_NullableToNullable_Null_TranslatesViaPostgresAsync()
    {
        OrderDtoWithNullableStatus result = await _db.NullableOrders
            .Where(o => o.Id == 4)
            .Project(_mappers.NullableToNullable)
            .SingleAsync();

        Assert.Null(result.Status);
    }

    [Theory]
    [InlineData(1, OrderStatusDto.Pending)]
    [InlineData(2, OrderStatusDto.Shipped)]
    [InlineData(3, OrderStatusDto.Cancelled)]
    public async Task Project_NullableToNonNullable_NonNull_TranslatesViaPostgresAsync(
        int id, OrderStatusDto expected)
    {
        OrderDtoWithDefaultStatus result = await _db.NullableOrders
            .Where(o => o.Id == id)
            .Project(_mappers.NullableToNonNullable)
            .SingleAsync();

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Project_NullableToNonNullable_Null_TranslatesViaPostgresAsync()
    {
        OrderDtoWithDefaultStatus result = await _db.NullableOrders
            .Where(o => o.Id == 4)
            .Project(_mappers.NullableToNonNullable)
            .SingleAsync();

        Assert.Equal(OrderStatusDto.Pending, result.Status);
    }

    [Fact]
    public async Task Select_ToNullableExpression_NonNull_TranslatesViaPostgresAsync()
    {
        Expression<Func<OrderStatus?, OrderStatusDto?>> expr = _mappers.OrderStatusMapper.ToNullableExpression();

        OrderStatusDto? result = await _db.NullableOrders
            .Where(o => o.Id == 1)
            .Select(o => o.Status)
            .Select(expr)
            .SingleAsync();

        Assert.Equal(OrderStatusDto.Pending, result);
    }

    [Fact]
    public async Task Select_ToNullableExpression_Null_TranslatesViaPostgresAsync()
    {
        Expression<Func<OrderStatus?, OrderStatusDto?>> expr = _mappers.OrderStatusMapper.ToNullableExpression();

        OrderStatusDto? result = await _db.NullableOrders
            .Where(o => o.Id == 4)
            .Select(o => o.Status)
            .Select(expr)
            .SingleAsync();

        Assert.Null(result);
    }
}

// ---------------------------------------------------------------------------
// Minimal DbContext for PostgreSQL-based enum translation tests
// ---------------------------------------------------------------------------

internal sealed class PostgresTestDbContext(DbContextOptions<PostgresTestDbContext> options) : DbContext(options)
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
        modelBuilder.Entity<RealEstateProperty>(e => e.HasKey(p => p.Id));
        modelBuilder.Entity<OrderWithNullableStatus>(e => e.HasKey(o => o.Id));
    }
}
