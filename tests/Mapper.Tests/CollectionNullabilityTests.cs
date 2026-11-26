using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that a collection mapping's source and destination nullability must
/// match: a source collection declared nullable cannot feed a non-nullable
/// destination (rejected at build time), and a nullable source feeding a nullable
/// destination maps <see langword="null"/> to <see langword="null"/> in memory
/// instead of throwing.
/// </summary>
public sealed class CollectionNullabilityTests
{
    // -----------------------------------------------------------------------
    // Build time — nullable source into a non-nullable destination
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_NullableSourceProjectedIntoNonNullableDestination_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new NullabilityMappers().BuildProjectedIntoNonNullable());

        Assert.Contains(nameof(NonNullableLinesDto.Lines), ex.Message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(NullableLinesSource)}.{nameof(NullableLinesSource.Lines)}", ex.Message, StringComparison.Ordinal);
        Assert.Contains("declared nullable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NullableSourceAssignedToNonNullableDestination_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new NullabilityMappers().BuildAssignedIntoNonNullable());

        Assert.Contains(nameof(NonNullableTagsDto.Tags), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NullableSourceSelectedIntoNonNullableDestination_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new NullabilityMappers().BuildSelectedIntoNonNullable());

        Assert.Contains(nameof(NonNullableTagsDto.Tags), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NullableSourceIntoNonNullableOptionalDestination_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new NullabilityMappers().BuildOptionalIntoNonNullable());

        Assert.Contains(nameof(NonNullableLinesDto.Lines), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NonNullableSourceIntoNullableDestination_Succeeds()
    {
        Mapper<NonNullableLinesSource, NullableLinesDto> mapper = new NullabilityMappers().BuildNonNullableIntoNullable();

        NullableLinesDto? dest = mapper.Map(new NonNullableLinesSource { Lines = [new LineSource { Label = "a" }] });

        Assert.Equal("a", Assert.Single(dest.Lines!).Label);
    }

    // -----------------------------------------------------------------------
    // In memory — nullable source into a nullable destination
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NullableSourceIntoNullableDestination_NullSource_MapsToNull()
    {
        NullableLinesDto? dest = new NullabilityMappers().NullableIntoNullable.Map(new NullableLinesSource { Lines = null });

        Assert.NotNull(dest);
        Assert.Null(dest.Lines);
    }

    [Fact]
    public void Map_NullableSourceIntoNullableDestination_WithItems_MapsItems()
    {
        NullableLinesDto? dest = new NullabilityMappers().NullableIntoNullable.Map(
            new NullableLinesSource { Lines = [new LineSource { Label = "a" }, new LineSource { Label = "b" }] });

        Assert.Equal(["a", "b"], dest.Lines!.Select(l => l.Label));
    }

    [Fact]
    public void Map_NestedNullableSourceIntoNullableDestination_NullSource_MapsToNull()
    {
        var source = new NullableLinesHolder { Inner = new NullableLinesSource { Lines = null } };

        NullableLinesHolderDto? dest = new NullabilityMappers().Holder.Map(source);

        Assert.NotNull(dest.Inner);
        Assert.Null(dest.Inner.Lines);
    }

    [Fact]
    public void Map_CoalescedNullableSourceIntoNonNullableDestination_MapsToEmpty()
    {
        NonNullableLinesDto? dest = new NullabilityMappers().Coalesced.Map(new NullableLinesSource { Lines = null });

        Assert.NotNull(dest);
        Assert.Empty(dest.Lines);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — the in-memory guard is not emitted; EF materialises an
    // empty collection for a navigation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_NullableNavigationIntoNullableDestination_TranslatesAsync()
    {
        DbContextOptions<CollectionNullabilityDbContext> options =
            new DbContextOptionsBuilder<CollectionNullabilityDbContext>()
                .UseSqlite("DataSource=:memory:")
                .Options;

        await using var db = new CollectionNullabilityDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        db.Parents.AddRange(
            new NullableLinesEntity { Id = 1, Lines = [new LineEntity { Id = 1, Label = "a" }] },
            new NullableLinesEntity { Id = 2 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        List<NullableLinesDto> results = await db.Parents
            .OrderBy(p => p.Id)
            .Project(new NullabilityMappers().Entity)
            .ToListAsync();

        Assert.Equal("a", Assert.Single(results[0].Lines!).Label);
        Assert.Empty(results[1].Lines!);
    }
}

// ---------------------------------------------------------------------------
// Test-local models
// ---------------------------------------------------------------------------

file sealed class LineSource
{
    public required string Label { get; set; }
}

file sealed class LineDto
{
    public required string Label { get; set; }
}

file sealed class NullableLinesSource
{
    public List<LineSource>? Lines { get; set; }
    public List<string>? Tags { get; set; }
}

file sealed class NonNullableLinesSource
{
    public List<LineSource> Lines { get; set; } = [];
}

file sealed class NullableLinesHolder
{
    public required NullableLinesSource Inner { get; set; }
}

file sealed class NonNullableLinesDto
{
    public List<LineDto> Lines { get; set; } = [];
}

file sealed class NonNullableTagsDto
{
    public List<string> Tags { get; set; } = [];
}

file sealed class NullableLinesDto
{
    public List<LineDto>? Lines { get; set; }
}

file sealed class NullableLinesHolderDto
{
    public required NullableLinesDto Inner { get; set; }
}

internal sealed class NullableLinesEntity
{
    public int Id { get; set; }
    public List<LineEntity>? Lines { get; set; }
}

internal sealed class LineEntity
{
    public int Id { get; set; }
    public required string Label { get; set; }
}

// ---------------------------------------------------------------------------
// Mappers
// ---------------------------------------------------------------------------

file sealed class NullabilityMappers : MapperContext
{
    public NullabilityMappers()
    {
        Line = CreateMapper<LineSource, LineDto>(s => new LineDto { Label = s.Label });

        NullableIntoNullable = CreateMapper<NullableLinesSource, NullableLinesDto>(s => new NullableLinesDto
        {
            Lines = s.Lines!.Project(Line).ToList(),
        });

        Holder = CreateMapper<NullableLinesHolder, NullableLinesHolderDto>(s => new NullableLinesHolderDto
        {
            Inner = NullableIntoNullable.Map(s.Inner),
        });

        Coalesced = CreateMapper<NullableLinesSource, NonNullableLinesDto>(s => new NonNullableLinesDto
        {
            Lines = (s.Lines ?? new List<LineSource>()).Project(Line).ToList(),
        });

        Entity = CreateMapper<NullableLinesEntity, NullableLinesDto>(s => new NullableLinesDto
        {
            Lines = s.Lines!.Select(l => new LineDto { Label = l.Label }).ToList(),
        });
    }

    public Mapper<LineSource, LineDto> Line { get; }
    public Mapper<NullableLinesSource, NullableLinesDto> NullableIntoNullable { get; }
    public Mapper<NullableLinesHolder, NullableLinesHolderDto> Holder { get; }
    public Mapper<NullableLinesSource, NonNullableLinesDto> Coalesced { get; }
    public Mapper<NullableLinesEntity, NullableLinesDto> Entity { get; }

    public Mapper<NullableLinesSource, NonNullableLinesDto> BuildProjectedIntoNonNullable()
        => CreateMapper<NullableLinesSource, NonNullableLinesDto>(s => new NonNullableLinesDto
        {
            Lines = s.Lines!.Project(Line).ToList(),
        });

    public Mapper<NullableLinesSource, NonNullableTagsDto> BuildAssignedIntoNonNullable()
        => CreateMapper<NullableLinesSource, NonNullableTagsDto>(s => new NonNullableTagsDto
        {
            Tags = s.Tags!,
        });

    public Mapper<NullableLinesSource, NonNullableTagsDto> BuildSelectedIntoNonNullable()
        => CreateMapper<NullableLinesSource, NonNullableTagsDto>(s => new NonNullableTagsDto
        {
            Tags = s.Lines!.Select(l => l.Label).ToList(),
        });

    public Mapper<NullableLinesSource, NonNullableLinesDto> BuildOptionalIntoNonNullable()
        => CreateMapper<NullableLinesSource, NonNullableLinesDto>()
            .Optional(d => d.Lines, s => s.Lines!.Project(Line).ToList());

    public Mapper<NonNullableLinesSource, NullableLinesDto> BuildNonNullableIntoNullable()
        => CreateMapper<NonNullableLinesSource, NullableLinesDto>(s => new NullableLinesDto
        {
            Lines = s.Lines.Project(Line).ToList(),
        });
}

internal sealed class CollectionNullabilityDbContext(DbContextOptions<CollectionNullabilityDbContext> options)
    : DbContext(options)
{
    public DbSet<NullableLinesEntity> Parents => Set<NullableLinesEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NullableLinesEntity>().HasMany(p => p.Lines).WithOne().HasForeignKey("ParentId");
    }
}
