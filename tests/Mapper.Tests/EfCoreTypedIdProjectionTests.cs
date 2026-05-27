using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Models.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Coverage matrix for nested mappers projecting through EF Core against
/// entities keyed by <see cref="Id{T}"/> (value-converted PKs). Fills the
/// gaps that masked the <c>EF.Property&lt;Id&lt;T&gt;?&gt;(default(Nav), "Id")</c>
/// translation bug:
/// <list type="bullet">
/// <item>Optional <c>HasOne</c> + nested <c>Map()</c> (present and null source).</item>
/// <item>Required <c>HasOne</c> + nested <c>Map()</c>.</item>
/// <item>Required and optional <c>OwnsOne</c> + nested <c>Map()</c>.</item>
/// <item><c>HasMany</c> collection projected via <c>.Project(mapper).ToList()</c>.</item>
/// </list>
/// Uses Sqlite in-memory so the relational SQL translator runs (the in-memory
/// provider would not surface translator-layer mismatches).
/// </summary>
public sealed class EfCoreTypedIdProjectionTests : IDisposable
{
    private readonly TypedIdDbContext _db;
    private readonly TypedIdMappers   _mappers = new();

    private static readonly Id<Journalist>  _aliceId             = Id<Journalist>.New();
    private static readonly Id<FactChecker> _bobId               = Id<FactChecker>.New();
    private static readonly Id<Article>     _articleWithChecker  = Id<Article>.New();
    private static readonly Id<Article>     _articleNoChecker    = Id<Article>.New();
    private static readonly Id<Magazine>    _magazineId          = Id<Magazine>.New();

    public EfCoreTypedIdProjectionTests()
    {
        DbContextOptions<TypedIdDbContext> options = new DbContextOptionsBuilder<TypedIdDbContext>()
            .UseSqlite("DataSource=:memory:")
            .UseArchPillarTypedIds()
            .Options;

        _db = new TypedIdDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        Seed(_db);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // -----------------------------------------------------------------------
    // Optional HasOne — nested Map() on a null source returns null,
    // on a non-null source returns a populated DTO.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_OptionalHasOne_PresentSource_ProducesPopulatedDtoAsync()
    {
        ArticleDto result = await _db.Articles
            .Where(a => a.Id == _articleWithChecker)
            .Project(_mappers.Article)
            .SingleAsync();

        Assert.NotNull(result.FactChecker);
        Assert.Equal("Bob", result.FactChecker!.Name);
    }

    [Fact]
    public async Task Project_OptionalHasOne_NullSource_ProducesNullDtoAsync()
    {
        ArticleDto result = await _db.Articles
            .Where(a => a.Id == _articleNoChecker)
            .Project(_mappers.Article)
            .SingleAsync();

        Assert.Null(result.FactChecker);
    }

    // -----------------------------------------------------------------------
    // Required HasOne — nested Map() always produces a populated DTO; the
    // null-branch of the inliner's IIF is statically elided by EF Core.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_RequiredHasOne_ProducesPopulatedDtoAsync()
    {
        List<ArticleDto> results = await _db.Articles.Project(_mappers.Article).ToListAsync();

        Assert.All(results, a => Assert.Equal("Alice", a.Journalist.Name));
    }

    // -----------------------------------------------------------------------
    // Required OwnsOne — nested Map() round-trips owned columns.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_RequiredOwnedType_ProducesPopulatedDtoAsync()
    {
        ArticleDto result = await _db.Articles
            .Where(a => a.Id == _articleWithChecker)
            .Project(_mappers.Article)
            .SingleAsync();

        Assert.Equal("with-checker", result.Slug.Value);
    }

    // -----------------------------------------------------------------------
    // Optional OwnsOne — present source populates the DTO; null source
    // produces a null DTO field.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_OptionalOwnedType_PresentSource_ProducesPopulatedDtoAsync()
    {
        ArticleDto result = await _db.Articles
            .Where(a => a.Id == _articleWithChecker)
            .Project(_mappers.Article)
            .SingleAsync();

        Assert.NotNull(result.Featured);
        Assert.Equal(7, result.Featured!.Priority);
    }

    [Fact]
    public async Task Project_OptionalOwnedType_NullSource_ProducesNullDtoAsync()
    {
        ArticleDto result = await _db.Articles
            .Where(a => a.Id == _articleNoChecker)
            .Project(_mappers.Article)
            .SingleAsync();

        Assert.Null(result.Featured);
    }

    // -----------------------------------------------------------------------
    // HasMany — parent mapper projects the child collection via the nested
    // mapper, producing the full chain Magazine -> Articles[] -> ...
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_HasManyCollection_PopulatesNestedDtosAsync()
    {
        MagazineDto result = await _db.Magazines
            .Include(m => m.Articles).ThenInclude(a => a.Journalist)
            .Include(m => m.Articles).ThenInclude(a => a.FactChecker)
            .Project(_mappers.Magazine)
            .SingleAsync();

        Assert.Equal(2, result.Articles.Count);
        Assert.Contains(result.Articles, a => a.FactChecker?.Name == "Bob");
        Assert.Contains(result.Articles, a => a.FactChecker == null);
        Assert.All(result.Articles, a => Assert.Equal("Alice", a.Journalist.Name));
    }

    // -----------------------------------------------------------------------
    // Hand-written Select(... mapper.Map(...)) — exercises the EF Core
    // MapperInliningInterceptor's null guard on the inlined call.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Select_HandWrittenMapCall_OptionalHasOne_TranslatesAsync()
    {
        List<ArticleDto> results = await _db.Articles
            .OrderBy(a => a.Id)
            .Select(a => _mappers.Article.Map(a)!)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, a => a.FactChecker != null);
        Assert.Contains(results, a => a.FactChecker == null);
    }

    private static void Seed(TypedIdDbContext db)
    {
        Journalist  alice = new() { Id = _aliceId, Name = "Alice" };
        FactChecker bob   = new() { Id = _bobId,   Name = "Bob"   };

        db.Journalists.Add(alice);
        db.FactCheckers.Add(bob);

        Article withChecker = new()
        {
            Id          = _articleWithChecker,
            Headline    = "Big news",
            Journalist  = alice,
            FactChecker = bob,
            Slug        = new Slug { Value = "with-checker" },
            Featured    = new Featured { Priority = 7 },
        };

        Article noChecker = new()
        {
            Id         = _articleNoChecker,
            Headline   = "Smaller news",
            Journalist = alice,
            Slug       = new Slug { Value = "no-checker" },
        };

        db.Articles.AddRange(withChecker, noChecker);

        db.Magazines.Add(new Magazine
        {
            Id       = _magazineId,
            Title    = "Weekly",
            Articles = [withChecker, noChecker],
        });
    }
}

// ---------------------------------------------------------------------------
// Source entities — all use Id<T> primary keys
// ---------------------------------------------------------------------------

public sealed class Magazine
{
    public required Id<Magazine> Id { get; set; }
    public required string Title { get; set; }
    public List<Article> Articles { get; set; } = [];
}

public sealed class Article
{
    public required Id<Article> Id { get; set; }
    public required string Headline { get; set; }
    public required Journalist Journalist { get; set; }   // required HasOne
    public FactChecker? FactChecker { get; set; }          // optional HasOne
    public required Slug Slug { get; set; }                // required OwnsOne
    public Featured? Featured { get; set; }                // optional OwnsOne
}

public sealed class Journalist
{
    public required Id<Journalist> Id { get; set; }
    public required string Name { get; set; }
}

public sealed class FactChecker
{
    public required Id<FactChecker> Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Slug
{
    public required string Value { get; set; }
}

public sealed class Featured
{
    public required int Priority { get; set; }
}

// ---------------------------------------------------------------------------
// Destination DTOs
// ---------------------------------------------------------------------------

public sealed class MagazineDto
{
    public required Id<Magazine> Id { get; set; }
    public required string Title { get; set; }
    public List<ArticleDto> Articles { get; set; } = [];
}

public sealed class ArticleDto
{
    public required Id<Article> Id { get; set; }
    public required string Headline { get; set; }
    public required JournalistDto Journalist { get; set; }
    public FactCheckerDto? FactChecker { get; set; }
    public required SlugDto Slug { get; set; }
    public FeaturedDto? Featured { get; set; }
}

public sealed class JournalistDto
{
    public required Id<Journalist> Id { get; set; }
    public required string Name { get; set; }
}

public sealed class FactCheckerDto
{
    public required Id<FactChecker> Id { get; set; }
    public required string Name { get; set; }
}

public sealed class SlugDto
{
    public required string Value { get; set; }
}

public sealed class FeaturedDto
{
    public required int Priority { get; set; }
}

// ---------------------------------------------------------------------------
// Mappers — all nested calls exercise the inliner's null guard
// ---------------------------------------------------------------------------

public sealed class TypedIdMappers : MapperContext
{
    public Mapper<Journalist,  JournalistDto>  Journalist  { get; }
    public Mapper<FactChecker, FactCheckerDto> FactChecker { get; }
    public Mapper<Slug,        SlugDto>        Slug        { get; }
    public Mapper<Featured,    FeaturedDto>    Featured    { get; }
    public Mapper<Article,     ArticleDto>     Article     { get; }
    public Mapper<Magazine,    MagazineDto>    Magazine    { get; }

    public TypedIdMappers()
    {
        Journalist = CreateMapper<Journalist, JournalistDto>(src => new JournalistDto
        {
            Id   = src.Id,
            Name = src.Name,
        });

        FactChecker = CreateMapper<FactChecker, FactCheckerDto>(src => new FactCheckerDto
        {
            Id   = src.Id,
            Name = src.Name,
        });

        Slug = CreateMapper<Slug, SlugDto>(src => new SlugDto
        {
            Value = src.Value,
        });

        Featured = CreateMapper<Featured, FeaturedDto>(src => new FeaturedDto
        {
            Priority = src.Priority,
        });

        Article = CreateMapper<Article, ArticleDto>(src => new ArticleDto
        {
            Id          = src.Id,
            Headline    = src.Headline,
            Journalist  = Journalist.Map(src.Journalist),
            FactChecker = FactChecker.Map(src.FactChecker),
            Slug        = Slug.Map(src.Slug),
            Featured    = Featured.Map(src.Featured),
        });

        Magazine = CreateMapper<Magazine, MagazineDto>(src => new MagazineDto
        {
            Id       = src.Id,
            Title    = src.Title,
            Articles = src.Articles.Project(Article).ToList(),
        });

        EagerBuildAll();
    }
}

// ---------------------------------------------------------------------------
// DbContext — Id<T> auto-registered by UseArchPillarTypedIds, plus the mix
// of required / optional reference navs and same-table owned types.
// ---------------------------------------------------------------------------

internal sealed class TypedIdDbContext(DbContextOptions<TypedIdDbContext> options) : DbContext(options)
{
    public DbSet<Magazine>    Magazines    => Set<Magazine>();
    public DbSet<Article>     Articles     => Set<Article>();
    public DbSet<Journalist>  Journalists  => Set<Journalist>();
    public DbSet<FactChecker> FactCheckers => Set<FactChecker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Magazine>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasMany(m => m.Articles).WithOne().HasForeignKey("MagazineId");
        });

        modelBuilder.Entity<Article>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne(a => a.Journalist).WithMany().IsRequired();
            e.HasOne(a => a.FactChecker).WithMany().IsRequired(false);
            e.OwnsOne(a => a.Slug);
            e.OwnsOne(a => a.Featured);
        });

        modelBuilder.Entity<Journalist>(e => e.HasKey(j => j.Id));
        modelBuilder.Entity<FactChecker>(e => e.HasKey(f => f.Id));
    }
}
