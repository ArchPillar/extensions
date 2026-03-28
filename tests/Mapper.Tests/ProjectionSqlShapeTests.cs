using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Verifies that projection expressions involving scalar null guards,
/// collection projections, and optional collection projections translate
/// correctly to SQL via a real provider (SQLite).
/// </summary>
public sealed class ProjectionSqlShapeTests : IDisposable
{
    private readonly SqlShapeDbContext _db;

    public ProjectionSqlShapeTests()
    {
        DbContextOptions<SqlShapeDbContext> options = new DbContextOptionsBuilder<SqlShapeDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new SqlShapeDbContext(options);
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
    // Scalar null guard — nullable navigation with nested mapper
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_NullableNavigation_NonNull_TranslatesCorrectlyAsync()
    {
        var mappers = new SqlShapeMappers();

        SqlShapeParentDto result = await _db.Parents
            .Where(p => p.Id == 1)
            .Project(mappers.Parent)
            .SingleAsync();

        Assert.Equal("HQ", result.Name);
        Assert.NotNull(result.Child);
        Assert.Equal("Alice", result.Child!.Label);
    }

    [Fact]
    public async Task Project_NullableNavigation_Null_TranslatesCorrectlyAsync()
    {
        var mappers = new SqlShapeMappers();

        SqlShapeParentDto result = await _db.Parents
            .Where(p => p.Id == 2)
            .Project(mappers.Parent)
            .SingleAsync();

        Assert.Equal("Branch", result.Name);
        Assert.Null(result.Child);
    }

    // -----------------------------------------------------------------------
    // Collection projection — required collection with Project()
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_Collection_WithItems_TranslatesCorrectlyAsync()
    {
        var mappers = new SqlShapeMappers();

        SqlShapeParentDto result = await _db.Parents
            .Where(p => p.Id == 1)
            .Project(mappers.Parent)
            .SingleAsync();

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.Tag == "widget");
        Assert.Contains(result.Items, i => i.Tag == "gadget");
    }

    [Fact]
    public async Task Project_Collection_Empty_TranslatesCorrectlyAsync()
    {
        var mappers = new SqlShapeMappers();

        SqlShapeParentDto result = await _db.Parents
            .Where(p => p.Id == 2)
            .Project(mappers.Parent)
            .SingleAsync();

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
    }

    // -----------------------------------------------------------------------
    // Optional collection — excluded and included
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_OptionalCollection_Excluded_IsNullAsync()
    {
        var mappers = new SqlShapeMappers();

        SqlShapeParentDto result = await _db.Parents
            .Where(p => p.Id == 1)
            .Project(mappers.Parent)
            .SingleAsync();

        Assert.Null(result.LowStock);
    }

    [Fact]
    public async Task Project_OptionalCollection_Included_TranslatesCorrectlyAsync()
    {
        var mappers = new SqlShapeMappers();

        SqlShapeParentDto result = await _db.Parents
            .Where(p => p.Id == 1)
            .Project(mappers.Parent, o => o.Include(d => d.LowStock))
            .SingleAsync();

        Assert.NotNull(result.LowStock);
        Assert.Single(result.LowStock);
        Assert.Equal("gadget", result.LowStock[0].Tag);
    }

    // -----------------------------------------------------------------------
    // Seed helpers
    // -----------------------------------------------------------------------

    private static void SeedData(SqlShapeDbContext db)
    {
        db.Parents.AddRange(
            new SqlShapeParent
            {
                Id = 1,
                Name = "HQ",
                Child = new SqlShapeChild { Id = 1, Label = "Alice" },
                Items =
                [
                    new SqlShapeItem { Id = 1, Tag = "widget", Quantity = 100 },
                    new SqlShapeItem { Id = 2, Tag = "gadget", Quantity = 3 },
                ],
            },
            new SqlShapeParent
            {
                Id = 2,
                Name = "Branch",
                Child = null,
                Items = [],
            });

        db.SaveChanges();
    }
}

// ---------------------------------------------------------------------------
// Test-local models — entities (internal for EF Core reflection)
// ---------------------------------------------------------------------------

internal class SqlShapeParent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ChildId { get; set; }
    public SqlShapeChild? Child { get; set; }
    public List<SqlShapeItem> Items { get; set; } = [];
}

internal class SqlShapeChild
{
    public int Id { get; set; }
    public required string Label { get; set; }
}

internal class SqlShapeItem
{
    public int Id { get; set; }
    public required string Tag { get; set; }
    public int Quantity { get; set; }
}

// ---------------------------------------------------------------------------
// Test-local DTOs and mapper
// ---------------------------------------------------------------------------

file class SqlShapeParentDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public SqlShapeChildDto? Child { get; set; }
    public List<SqlShapeItemDto> Items { get; set; } = [];
    public List<SqlShapeItemDto>? LowStock { get; set; }
}

file class SqlShapeChildDto
{
    public required string Label { get; set; }
}

file class SqlShapeItemDto
{
    public required string Tag { get; set; }
    public required int Quantity { get; set; }
}

file class SqlShapeMappers : MapperContext
{
    public Mapper<SqlShapeChild, SqlShapeChildDto> Child { get; }
    public Mapper<SqlShapeItem, SqlShapeItemDto> Item { get; }
    public Mapper<SqlShapeParent, SqlShapeParentDto> Parent { get; }

    public SqlShapeMappers()
    {
        Child = CreateMapper<SqlShapeChild, SqlShapeChildDto>(s => new SqlShapeChildDto
        {
            Label = s.Label,
        });

        Item = CreateMapper<SqlShapeItem, SqlShapeItemDto>(s => new SqlShapeItemDto
        {
            Tag = s.Tag,
            Quantity = s.Quantity,
        });

        Parent = CreateMapper<SqlShapeParent, SqlShapeParentDto>(s => new SqlShapeParentDto
        {
            Id = s.Id,
            Name = s.Name,
            Child = Child.Map(s.Child),
            Items = s.Items.Project(Item).ToList(),
        })
        .Optional(d => d.LowStock, s => s.Items.Where(i => i.Quantity < 10).Project(Item).ToList());
    }
}

// ---------------------------------------------------------------------------
// Minimal DbContext for SQL shape tests
// ---------------------------------------------------------------------------

internal sealed class SqlShapeDbContext(DbContextOptions<SqlShapeDbContext> options) : DbContext(options)
{
    public DbSet<SqlShapeParent> Parents => Set<SqlShapeParent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlShapeParent>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.Child).WithMany().HasForeignKey(p => p.ChildId);
            e.HasMany(p => p.Items).WithOne().HasForeignKey("ParentId");
        });

        modelBuilder.Entity<SqlShapeChild>(e => e.HasKey(c => c.Id));
        modelBuilder.Entity<SqlShapeItem>(e => e.HasKey(i => i.Id));
    }
}
