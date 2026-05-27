using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Reproduction for an EF Core relational-translation failure when a nested
/// <see cref="Mapper{TSource,TDest}.Map(TSource)"/> projects an <b>optional</b>
/// navigation to an entity whose key uses a value converter (a strongly-typed id,
/// e.g. <c>Id&lt;T&gt;</c>).
/// <para>
/// When inlining a nested <c>Map(src.Nav)</c> for a reference-type source,
/// <c>NestedMapperInliner</c> emits the null guard as
/// <c>src.Nav == default(NavType) ? null : new Dto { ... }</c> —
/// i.e. <c>Expression.Equal(srcExpr, Expression.Default(srcExpr.Type))</c>.
/// EF Core's entity-equality rewrite then tries to read the key off that
/// <c>default(NavType)</c> node (<c>EF.Property&lt;TKey&gt;(default(Nav), "Id")</c>)
/// instead of treating it as a SQL <c>NULL</c>, and throws:
/// </para>
/// <code>
/// Translation of 'EF.Property&lt;NavId?&gt;(default(Nav), "Id")' failed.
/// Either the query source is not an entity type, or the specified property
/// does not exist on the entity type.
/// </code>
/// <para>
/// AutoMapper's <c>ProjectTo</c> builds a translatable guard for the same shape
/// (it compares against a typed <c>null</c> constant, which EF folds into an FK
/// <c>IS NULL</c> check). Emitting <c>Expression.Constant(null, type)</c> rather
/// than <c>Expression.Default(type)</c> for reference-type sources is expected to fix it.
/// </para>
/// <para>
/// The value-converted key is load-bearing: with a plain CLR key EF tends to fold the
/// comparison, but a strongly-typed (value-converted) key forces the entity-equality
/// rewrite that surfaces the bug — which is the common real-world case.
/// </para>
/// </summary>
public sealed class EfCoreOptionalEntityProjectionTests : IDisposable
{
    private readonly OptionalNavDbContext _db;
    private readonly OptionalNavMappers _mappers = new();

    public EfCoreOptionalEntityProjectionTests()
    {
        DbContextOptionsBuilder<OptionalNavDbContext> builder =
            new DbContextOptionsBuilder<OptionalNavDbContext>()
                .UseSqlite("DataSource=:memory:");
        builder.UseArchPillarMapper();

        _db = new OptionalNavDbContext(builder.Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        // One ticket WITH a reviewer, one WITHOUT (optional nav is null).
        var reviewer = new Reviewer { Id = new ReviewerId(Guid.NewGuid()), Name = "Alice" };
        _db.Submissions.AddRange(
            new Submission { Id = 1, Title = "reviewed", Reviewer = reviewer },
            new Submission { Id = 2, Title = "unreviewed", Reviewer = null });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task ProjectingOptionalEntityNav_ViaNestedMap_TranslatesToSqlAsync()
    {
        List<SubmissionDto> results = await _db.Submissions
            .OrderBy(t => t.Id)
            .Select(t => _mappers.Submission.Map(t)!)
            .ToListAsync();

        Assert.Equal(2, results.Count);

        // Present navigation maps to a populated DTO.
        Assert.NotNull(results[0].Reviewer);
        Assert.Equal("Alice", results[0].Reviewer!.Name);

        // Null navigation maps to a null DTO.
        Assert.Null(results[1].Reviewer);
    }
}

// ---------------------------------------------------------------------------
// Source models — Reviewer has a value-converted (strongly-typed) key
// ---------------------------------------------------------------------------

public readonly record struct ReviewerId(Guid Value);

public sealed class Reviewer
{
    public required ReviewerId Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Submission
{
    public required int Id { get; set; }
    public required string Title { get; set; }
    public Reviewer? Reviewer { get; set; }   // optional navigation
}

// ---------------------------------------------------------------------------
// Destination DTOs
// ---------------------------------------------------------------------------

public sealed class ReviewerDto
{
    public required string Name { get; set; }
}

public sealed class SubmissionDto
{
    public required int Id { get; set; }
    public required string Title { get; set; }
    public ReviewerDto? Reviewer { get; set; }   // null when the source nav is null
}

// ---------------------------------------------------------------------------
// Mappers — SubmissionDto.Reviewer is produced by a nested Map() of the optional nav
// ---------------------------------------------------------------------------

public sealed class OptionalNavMappers : MapperContext
{
    public Mapper<Reviewer, ReviewerDto> Reviewer { get; }
    public Mapper<Submission, SubmissionDto> Submission { get; }

    public OptionalNavMappers()
    {
        Reviewer = CreateMapper<Reviewer, ReviewerDto>(src => new ReviewerDto
        {
            Name = src.Name,
        });

        Submission = CreateMapper<Submission, SubmissionDto>(src => new SubmissionDto
        {
            Id = src.Id,
            Title = src.Title,
            Reviewer = Reviewer.Map(src.Reviewer),   // nested OPTIONAL entity → null guard
        });

        EagerBuildAll();
    }
}

// ---------------------------------------------------------------------------
// DbContext — value converter on the strongly-typed key; optional FK
// ---------------------------------------------------------------------------

internal sealed class OptionalNavDbContext(DbContextOptions<OptionalNavDbContext> options)
    : DbContext(options)
{
    public DbSet<Submission> Submissions => Set<Submission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reviewer>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id)
                .HasConversion(new ValueConverter<ReviewerId, Guid>(
                    id => id.Value,
                    value => new ReviewerId(value)));
        });

        modelBuilder.Entity<Submission>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Reviewer)
                .WithMany()
                .HasForeignKey("ReviewerId")
                .IsRequired(false);
        });
    }
}
