using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper.Tests;

public class InheritedMapperTests
{
    private readonly InheritanceMappers _mappers = new();

    private static Document CreateDocument() => new()
    {
        Id         = 1,
        Title      = "Design Patterns",
        Content    = "Full content here",
        Author     = "GoF",
        CreatedAt  = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        Category   = "Software",
        ViewCount  = 42,
        ReviewedBy = new Customer { Name = "Alice", Email = "alice@example.com" },
    };

    // -----------------------------------------------------------------------
    // Base mapper still works normally
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_BaseSummary_MapsBaseProperties()
    {
        var doc = CreateDocument();

        DocumentSummaryDto dto = _mappers.Summary.Map(doc)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal("Design Patterns", dto.Title);
        Assert.Equal("GoF", dto.Author);
    }

    // -----------------------------------------------------------------------
    // Inherited mapper: Detail inherits Summary + adds its own properties
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_InheritedDetail_MapsInheritedProperties()
    {
        var doc = CreateDocument();

        DocumentDetailDto dto = _mappers.Detail.Map(doc)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal("Design Patterns", dto.Title);
        Assert.Equal("GoF", dto.Author);
    }

    [Fact]
    public void Map_InheritedDetail_MapsDerivedProperties()
    {
        var doc = CreateDocument();

        DocumentDetailDto dto = _mappers.Detail.Map(doc)!;

        Assert.Equal("Full content here", dto.Content);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), dto.CreatedAt);
    }

    // -----------------------------------------------------------------------
    // Inherited mapper: Stats inherits Summary + adds ViewCount
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_InheritedStats_MapsInheritedAndDerivedProperties()
    {
        var doc = CreateDocument();

        DocumentStatsDto dto = _mappers.Stats.Map(doc)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal("Design Patterns", dto.Title);
        Assert.Equal("GoF", dto.Author);
        Assert.Equal(42, dto.ViewCount);
    }

    // -----------------------------------------------------------------------
    // Null source returns null
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NullSource_ReturnsNull()
    {
        Assert.Null(_mappers.Detail.Map(null));
        Assert.Null(_mappers.Stats.Map(null));
    }

    // -----------------------------------------------------------------------
    // Optional properties inherited from base
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_InheritedDetail_IncludesInheritedOptionalInMemory()
    {
        var doc = CreateDocument();

        DocumentDetailDto dto = _mappers.Detail.Map(doc)!;

        Assert.Equal("Software", dto.Category);
    }

    [Fact]
    public void Projection_InheritedDetail_ExcludesOptionalByDefault()
    {
        var doc = CreateDocument();

        var expression = _mappers.Detail.ToExpression();
        DocumentDetailDto dto = expression.Compile()(doc);

        Assert.Null(dto.Category);
        Assert.Null(dto.ReviewerName);
    }

    [Fact]
    public void Projection_InheritedDetail_IncludesInheritedOptionalWhenRequested()
    {
        var doc = CreateDocument();

        var expression = _mappers.Detail.ToExpression(o => o.Include(d => d.Category));
        DocumentDetailDto dto = expression.Compile()(doc);

        Assert.Equal("Software", dto.Category);
    }

    [Fact]
    public void Projection_InheritedDetail_IncludesDerivedOptionalWhenRequested()
    {
        var doc = CreateDocument();

        var expression = _mappers.Detail.ToExpression(o => o.Include(d => d.ReviewerName));
        DocumentDetailDto dto = expression.Compile()(doc);

        Assert.Equal("Alice", dto.ReviewerName);
    }

    // -----------------------------------------------------------------------
    // MapTo works on inherited mappers
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_InheritedDetail_AssignsAllProperties()
    {
        var doc = CreateDocument();
        var dto = new DocumentDetailDto
        {
            Id        = 0,
            Title     = "",
            Author    = "",
            Content   = "",
            CreatedAt = default,
        };

        _mappers.Detail.MapTo(doc, dto);

        Assert.Equal(1, dto.Id);
        Assert.Equal("Design Patterns", dto.Title);
        Assert.Equal("GoF", dto.Author);
        Assert.Equal("Full content here", dto.Content);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), dto.CreatedAt);
    }

    // -----------------------------------------------------------------------
    // Derived source + derived destination via For<TSource, TDest>()
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_DerivedSource_MapsInheritedAndDerivedProperties()
    {
        var mappers = new DerivedSourceMappers();
        var doc = new TechnicalDocument
        {
            Id         = 1,
            Title      = "C# in Depth",
            Content    = "Generics, LINQ, async",
            Author     = "Jon Skeet",
            CreatedAt  = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ViewCount  = 999,
            Language   = "C#",
            ReviewedBy = new Customer { Name = "Reviewer", Email = "r@example.com" },
        };

        TechnicalDocumentDto dto = mappers.Technical.Map(doc)!;

        // Inherited from Summary (grandparent)
        Assert.Equal(1, dto.Id);
        Assert.Equal("C# in Depth", dto.Title);
        Assert.Equal("Jon Skeet", dto.Author);

        // Inherited from Detail (parent)
        Assert.Equal("Generics, LINQ, async", dto.Content);
        Assert.Equal(new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), dto.CreatedAt);

        // Mapped on derived source
        Assert.Equal("C#", dto.Language);
    }

    [Fact]
    public void Projection_DerivedSource_Works()
    {
        var mappers = new DerivedSourceMappers();
        var doc = new TechnicalDocument
        {
            Id         = 1,
            Title      = "Rust Book",
            Content    = "Ownership and borrowing",
            Author     = "Community",
            CreatedAt  = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ViewCount  = 500,
            Language   = "Rust",
            ReviewedBy = new Customer { Name = "Rev", Email = "rev@example.com" },
        };

        var expression = mappers.Technical.ToExpression();
        TechnicalDocumentDto dto = expression.Compile()(doc);

        Assert.Equal("Rust Book", dto.Title);
        Assert.Equal("Ownership and borrowing", dto.Content);
        Assert.Equal("Rust", dto.Language);
    }

    [Fact]
    public void MapTo_DerivedSource_AssignsAllProperties()
    {
        var mappers = new DerivedSourceMappers();
        var doc = new TechnicalDocument
        {
            Id         = 5,
            Title      = "Go Handbook",
            Content    = "Goroutines",
            Author     = "Go Team",
            CreatedAt  = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            ViewCount  = 200,
            Language   = "Go",
            ReviewedBy = new Customer { Name = "X", Email = "x@example.com" },
        };

        var dto = new TechnicalDocumentDto
        {
            Id        = 0,
            Title     = "",
            Author    = "",
            Content   = "",
            CreatedAt = default,
            Language  = "",
        };

        mappers.Technical.MapTo(doc, dto);

        Assert.Equal(5, dto.Id);
        Assert.Equal("Go Handbook", dto.Title);
        Assert.Equal("Goroutines", dto.Content);
        Assert.Equal("Go", dto.Language);
    }

    // -----------------------------------------------------------------------
    // Fluent overrides on inherited builder (last wins)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_InheritedBuilder_CanOverrideBaseMapping()
    {
        var mappers = new OverrideInheritanceMappers();
        var doc = CreateDocument();

        DocumentDetailDto dto = mappers.Detail.Map(doc)!;

        Assert.Equal("DESIGN PATTERNS", dto.Title);
        Assert.Equal("Full content here", dto.Content);
    }

    // -----------------------------------------------------------------------
    // Eager build validates inherited mappers at startup
    // -----------------------------------------------------------------------

    [Fact]
    public void EagerBuild_InheritedMappers_CompilesWithoutError()
    {
        var mappers = new EagerInheritanceMappers();
        Assert.NotNull(mappers);
    }

    // -----------------------------------------------------------------------
    // Coverage validation catches unmapped properties on derived type
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_InheritedMapper_ThrowsWhenDerivedPropertyNotMapped()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new MissingPropertyMappers());

        Assert.Contains("ViewCount", exception.Message);
    }
}

// ---------------------------------------------------------------------------
// EF Core integration for inherited mappers
// ---------------------------------------------------------------------------

public sealed class InheritedMapperEfCoreTests : IDisposable
{
    private readonly DocumentDbContext  _db;
    private readonly InheritanceMappers _mappers = new();

    public InheritedMapperEfCoreTests()
    {
        DbContextOptions<DocumentDbContext> options = new DbContextOptionsBuilder<DocumentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new DocumentDbContext(options);
        SeedData(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Project_InheritedDetail_TranslatesToSqlAsync()
    {
        List<DocumentDetailDto> results = await _db.Documents
            .Include(d => d.ReviewedBy)
            .Project(_mappers.Detail)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        var first = results.Single(d => d.Id == 1);
        Assert.Equal("Design Patterns", first.Title);
        Assert.Equal("GoF", first.Author);
        Assert.Equal("Full content", first.Content);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), first.CreatedAt);
    }

    [Fact]
    public async Task Project_InheritedStats_TranslatesToSqlAsync()
    {
        List<DocumentStatsDto> results = await _db.Documents
            .Project(_mappers.Stats)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        var first = results.Single(d => d.Id == 1);
        Assert.Equal("Design Patterns", first.Title);
        Assert.Equal(100, first.ViewCount);
    }

    [Fact]
    public async Task Project_InheritedDetail_WithOptionalInclude_TranslatesToSqlAsync()
    {
        List<DocumentDetailDto> results = await _db.Documents
            .Include(d => d.ReviewedBy)
            .Project(_mappers.Detail, o => o.Include(d => d.ReviewerName))
            .ToListAsync();

        var first = results.Single(d => d.Id == 1);
        Assert.Equal("Alice", first.ReviewerName);
    }

    private static void SeedData(DocumentDbContext db)
    {
        db.Documents.AddRange(
            new Document
            {
                Id         = 1,
                Title      = "Design Patterns",
                Content    = "Full content",
                Author     = "GoF",
                CreatedAt  = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Category   = "Software",
                ViewCount  = 100,
                ReviewedBy = new Customer { Name = "Alice", Email = "alice@example.com" },
            },
            new Document
            {
                Id         = 2,
                Title      = "Clean Code",
                Content    = "More content",
                Author     = "Uncle Bob",
                CreatedAt  = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ViewCount  = 50,
                ReviewedBy = new Customer { Name = "Bob", Email = "bob@example.com" },
            });

        db.SaveChanges();
    }
}

internal class DocumentDbContext(DbContextOptions<DocumentDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasOne(d => d.ReviewedBy).WithOne().HasForeignKey<Customer>("DocumentId");
        });

        modelBuilder.Entity<Customer>(e => e.HasKey(c => c.Name));
    }
}

// ---------------------------------------------------------------------------
// Helper mapper contexts for specific test scenarios
// ---------------------------------------------------------------------------

/// <summary>
/// Demonstrates overriding a base mapping in the inherited builder.
/// </summary>
internal class OverrideInheritanceMappers : MapperContext
{
    public Mapper<Document, DocumentSummaryDto> Summary { get; }
    public Mapper<Document, DocumentDetailDto>  Detail  { get; }

    public OverrideInheritanceMappers()
    {
        Summary = CreateMapper<Document, DocumentSummaryDto>(src => new DocumentSummaryDto
        {
            Id     = src.Id,
            Title  = src.Title,
            Author = src.Author,
        });

        Detail = Inherit(Summary).For<DocumentDetailDto>()
            .Map(dest => dest.Title, src => src.Title.ToUpper())
            .Map(dest => dest.Content, src => src.Content)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt);
    }
}

/// <summary>
/// Deliberately omits a required property on the derived type to verify
/// coverage validation catches it.
/// </summary>
internal class MissingPropertyMappers : MapperContext
{
    public Mapper<Document, DocumentSummaryDto> Summary { get; }
    public Mapper<Document, DocumentStatsDto>   Stats   { get; }

    public MissingPropertyMappers()
    {
        Summary = CreateMapper<Document, DocumentSummaryDto>(src => new DocumentSummaryDto
        {
            Id     = src.Id,
            Title  = src.Title,
            Author = src.Author,
        });

        // Missing: .Map(dest => dest.ViewCount, src => src.ViewCount)
        Stats = Inherit(Summary).For<DocumentStatsDto>();
    }
}
