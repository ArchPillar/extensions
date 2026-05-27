using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Probes the interaction between the nested-mapper inliner and EF Core's
/// owned-type translation. The inliner wraps every reference-type nested call
/// in <c>IIF(src.Owned == null, default, new Dto { ... })</c>; EF Core has to
/// rewrite that into something the database can evaluate (either eliding the
/// branch entirely, projecting null indicator columns, or — for split-table
/// owned types — an ID-column null check on the joined table).
/// <para>
/// All scenarios below are expected to translate and return correct data on
/// modern EF Core (10.x). They serve as regression tests: if the inliner
/// changes the shape of its null guard, or if EF Core's owned-type translation
/// regresses, these surface it.
/// </para>
/// </summary>
[Collection("PostgreSQL")]
public sealed class OwnedTypeProjectionPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase  _postgres = null!;
    private OwnedTypeDbContext    _db       = null!;
    private readonly OwnedMappers _mappers  = new();

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);

        DbContextOptions<OwnedTypeDbContext> options = new DbContextOptionsBuilder<OwnedTypeDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        _db = new OwnedTypeDbContext(options);
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
    // Required OwnsOne, same table — null check is elided by EF Core
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_RequiredOwnedPremises_TranslatesViaPostgresAsync()
    {
        List<ShopDto> results = await _db.Shops.Project(_mappers.Shop).ToListAsync();

        ShopDto shop = results.Single(s => s.Id == 1);
        Assert.Equal("Main St 1", shop.Premises.Street);
    }

    // -----------------------------------------------------------------------
    // Required OwnsOne reached through a projected collection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_RequiredOwnedPremisesInProjectedCollection_TranslatesViaPostgresAsync()
    {
        List<CompanyDto> results = await _db.Companies
            .Include(c => c.Shops)
            .Project(_mappers.Company)
            .ToListAsync();

        CompanyDto acme = results.Single(c => c.Id == 1);
        Assert.Single(acme.Shops);
        Assert.Equal("Main St 1", acme.Shops[0].Premises.Street);
    }

    // -----------------------------------------------------------------------
    // Filter on a projected owned-type field — exercises a follow-on Where
    // that depends on columns produced by the inlined IIF branch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_RequiredOwnedPremises_FilterOnProjectedField_TranslatesViaPostgresAsync()
    {
        List<ShopDto> results = await _db.Shops
            .Project(_mappers.Shop)
            .Where(s => s.Premises.City == "Springfield")
            .ToListAsync();

        Assert.Single(results);
    }

    // -----------------------------------------------------------------------
    // Nested OwnsOne (Premises owns Coordinates) — two levels of inlined
    // null guards stacked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_NestedOwnedTypes_TwoLevelsDeep_TranslatesViaPostgresAsync()
    {
        List<ShopDto> results = await _db.Shops.Project(_mappers.Shop).ToListAsync();

        ShopDto shop = results.Single(s => s.Id == 1);
        Assert.Equal(40.0, shop.Premises.Coordinates.Latitude);
        Assert.Equal(-74.0, shop.Premises.Coordinates.Longitude);
    }

    private static void SeedData(OwnedTypeDbContext db)
    {
        db.Companies.Add(new Company
        {
            Id   = 1,
            Name = "Acme",
            Shops =
            [
                new Shop
                {
                    Id       = 1,
                    Number   = "S-001",
                    Premises = new Premises
                    {
                        Street      = "Main St 1",
                        City        = "Springfield",
                        Country     = "USA",
                        Coordinates = new Coordinates { Latitude = 40.0, Longitude = -74.0 },
                    },
                },
            ],
        });
    }
}

// ---------------------------------------------------------------------------
// Source entities
// ---------------------------------------------------------------------------

public class Company
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public List<Shop> Shops { get; set; } = [];
}

public class Shop
{
    public required int Id { get; set; }
    public required string Number { get; set; }
    public required Premises Premises { get; set; }
}

public class Premises
{
    public required string Street { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public required Coordinates Coordinates { get; set; }
}

public class Coordinates
{
    public required double Latitude { get; set; }
    public required double Longitude { get; set; }
}

// ---------------------------------------------------------------------------
// Destination DTOs
// ---------------------------------------------------------------------------

public class CompanyDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public List<ShopDto> Shops { get; set; } = [];
}

public class ShopDto
{
    public required int Id { get; set; }
    public required string Number { get; set; }
    public required PremisesDto Premises { get; set; }
}

public class PremisesDto
{
    public required string Street { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public required CoordinatesDto Coordinates { get; set; }
}

public class CoordinatesDto
{
    public required double Latitude { get; set; }
    public required double Longitude { get; set; }
}

public sealed class OwnedMappers : MapperContext
{
    public Mapper<Coordinates, CoordinatesDto> Coordinates { get; }
    public Mapper<Premises,    PremisesDto>    Premises    { get; }
    public Mapper<Shop,        ShopDto>        Shop        { get; }
    public Mapper<Company,     CompanyDto>     Company     { get; }

    public OwnedMappers()
    {
        Coordinates = CreateMapper<Coordinates, CoordinatesDto>(src => new CoordinatesDto
        {
            Latitude  = src.Latitude,
            Longitude = src.Longitude,
        });

        Premises = CreateMapper<Premises, PremisesDto>(src => new PremisesDto
        {
            Street      = src.Street,
            City        = src.City,
            Country     = src.Country,
            Coordinates = Coordinates.Map(src.Coordinates),
        });

        Shop = CreateMapper<Shop, ShopDto>(src => new ShopDto
        {
            Id       = src.Id,
            Number   = src.Number,
            Premises = Premises.Map(src.Premises),
        });

        Company = CreateMapper<Company, CompanyDto>(src => new CompanyDto
        {
            Id    = src.Id,
            Name  = src.Name,
            Shops = src.Shops.Project(Shop).ToList(),
        });
    }
}

internal sealed class OwnedTypeDbContext(DbContextOptions<OwnedTypeDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Shop>    Shops     => Set<Shop>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasMany(c => c.Shops).WithOne().HasForeignKey("CompanyId");
        });

        modelBuilder.Entity<Shop>(e =>
        {
            e.HasKey(s => s.Id);
            e.OwnsOne(s => s.Premises, p => p.OwnsOne(x => x.Coordinates));
        });
    }
}
