using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Verifies that enum mapping expressions are translatable by the Npgsql
/// (PostgreSQL) provider. Tries Testcontainers first; falls back to a
/// local PostgreSQL install when Docker is not available.
/// </summary>
public sealed class EnumMappingPostgresTests : IAsyncLifetime
{
    private const string LocalConnectionTemplate =
        "Host=localhost;Port=5432;Username=mapper_test;Password=mapper_test;Database=";

    private PostgreSqlContainer? _postgres;
    private PostgresTestDbContext _db = null!;
    private readonly TestMappers _mappers = new();

    public async Task InitializeAsync()
    {
        var connectionString = await TryStartContainerAsync()
                            ?? TryLocalPostgres()
                            ?? throw new InvalidOperationException(
                                   "No PostgreSQL instance available. " +
                                   "Either start Docker or install PostgreSQL locally " +
                                   "with user 'mapper_test' (password 'mapper_test') " +
                                   "and CREATE DATABASE permissions.");

        DbContextOptions<PostgresTestDbContext> options = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        _db = new PostgresTestDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        SeedData(_db);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Drop the database when running locally to avoid stale data across
        // test runs.  Container databases are destroyed with the container.
        if (_postgres is null)
        {
            await _db.Database.EnsureDeletedAsync();
        }

        await _db.DisposeAsync();

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    /// <summary>
    /// Attempts to start a Testcontainers PostgreSQL container.
    /// Returns the connection string on success, <see langword="null"/> if
    /// Docker is unavailable.
    /// </summary>
    private async Task<string?> TryStartContainerAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilMessageIsLogged("database system is ready to accept connections"))
                .Build();

            await _postgres.StartAsync();
            return _postgres.GetConnectionString();
        }
        catch
        {
            _postgres = null;
            return null;
        }
    }

    /// <summary>
    /// Attempts to connect to a local PostgreSQL instance using a unique
    /// database name per test run to avoid conflicts with concurrent or
    /// repeated runs.  Returns the connection string on success,
    /// <see langword="null"/> if the server is unreachable.
    /// </summary>
    private static string? TryLocalPostgres()
    {
        var databaseName = $"mapper_tests_{Guid.NewGuid():N}";
        const string adminConnectionString = LocalConnectionTemplate + "postgres";

        try
        {
            using var adminConnection = new NpgsqlConnection(adminConnectionString);
            adminConnection.Open();
            using NpgsqlCommand cmd = adminConnection.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            cmd.ExecuteNonQuery();

            return LocalConnectionTemplate + databaseName;
        }
        catch
        {
            return null;
        }
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
    // Enum array — each element in a primitive collection is mapped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_EnumArray_TranslatesViaPostgresAsync()
    {
        List<PropertyListingDto> results = await _db.Listings
            .OrderBy(l => l.Id)
            .Project(_mappers.PropertyListing)
            .ToListAsync();

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
    }
}

// ---------------------------------------------------------------------------
// Minimal DbContext for PostgreSQL-based enum translation tests
// ---------------------------------------------------------------------------

internal sealed class PostgresTestDbContext(DbContextOptions<PostgresTestDbContext> options) : DbContext(options)
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
