using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies <see cref="NullHandling"/> for in-memory mapping:
/// <see cref="NullHandling.Lenient"/> (the default) maps a <see langword="null"/>
/// nested object to <see langword="null"/> and a <see langword="null"/> source
/// collection to an empty collection, or to <see langword="null"/> when the
/// destination is declared nullable; <see cref="NullHandling.Strict"/> fails on a
/// <see langword="null"/> that the nullable annotations rule out. LINQ projection
/// is unaffected by either mode.
/// </summary>
public sealed class NullHandlingTests
{
    // -----------------------------------------------------------------------
    // Lenient (default)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_Lenient_NullCollectionIntoNonNullableDestination_MapsToEmpty()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);

        CollectionsDto? dest = mappers.Collections.Map(new ParentSource { Lines = null! });

        Assert.NotNull(dest);
        Assert.Empty(dest.Lines);
        Assert.Empty(dest.Labels);
    }

    [Fact]
    public void Map_Lenient_NullCollectionIntoNullableDestination_MapsToNull()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);

        CollectionsDto? dest = mappers.Collections.Map(new ParentSource { MaybeLines = null });

        Assert.NotNull(dest);
        Assert.Null(dest.MaybeLines);
    }

    [Fact]
    public void Map_Lenient_WithItems_MapsItems()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);
        var source = new ParentSource
        {
            Lines      = [new LineSource { Label = "a" }],
            MaybeLines = [new LineSource { Label = "b" }],
        };

        CollectionsDto? dest = mappers.Collections.Map(source);

        Assert.Equal("a", Assert.Single(dest.Lines).Label);
        Assert.Equal("a", Assert.Single(dest.Labels));
        Assert.Equal("b", Assert.Single(dest.MaybeLines!).Label);
    }

    [Fact]
    public void Map_Lenient_NullNestedObjects_MapToNull()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);

        ObjectsDto? dest = mappers.Objects.Map(new ParentSource { Child = null!, MaybeChild = null });

        Assert.NotNull(dest);
        Assert.Null(dest.Child);
        Assert.Null(dest.MaybeChild);
    }

    [Fact]
    public void Map_Lenient_NullCollectionInNestedMapper_MapsToEmpty()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);

        HolderDto? dest = mappers.Holder.Map(new HolderSource { Inner = new ParentSource { Lines = null! } });

        Assert.Empty(dest.Inner.Lines);
    }

    // -----------------------------------------------------------------------
    // Strict
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_Strict_NullNonNullableCollection_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => mappers.Collections.Map(new ParentSource { Lines = null! }));

        Assert.Contains($"{nameof(ParentSource)}.{nameof(ParentSource.Lines)}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"destination property '{nameof(CollectionsDto.Lines)}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NullHandling.Strict", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_Strict_NullNullableCollectionIntoNullableDestination_MapsToNull()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);

        CollectionsDto? dest = mappers.Collections.Map(new ParentSource { MaybeLines = null });

        Assert.NotNull(dest);
        Assert.Null(dest.MaybeLines);
    }

    [Fact]
    public void Map_Strict_NullNullableCollectionIntoNonNullableDestination_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => mappers.Asserted.Map(new ParentSource { MaybeLines = null }));

        Assert.Contains(
            $"destination property '{nameof(CollectionsDto.Lines)}' is declared non-nullable",
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Map_Strict_NullNonNullableNestedObject_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => mappers.Objects.Map(new ParentSource { Child = null! }));

        Assert.Contains($"{nameof(ParentSource)}.{nameof(ParentSource.Child)}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NullHandling.Strict", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_Strict_NullNullableNestedObject_MapsToNull()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);

        ObjectsDto? dest = mappers.Objects.Map(new ParentSource { MaybeChild = null });

        Assert.NotNull(dest);
        Assert.Null(dest.MaybeChild);
        Assert.Equal("child", dest.Child?.Name);
    }

    [Fact]
    public void Map_Strict_NullCollectionInNestedMapper_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);

        Assert.Throws<InvalidOperationException>(
            () => mappers.Holder.Map(new HolderSource { Inner = new ParentSource { Lines = null! } }));
    }

    [Fact]
    public void MapTo_Strict_DeepNullNonNullableCollection_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);
        var dest = new CollectionsDto { Lines = [new LineDto { Label = "existing" }] };

        Assert.Throws<InvalidOperationException>(
            () => mappers.Deep.MapTo(new ParentSource { Lines = null! }, dest));
    }

    [Fact]
    public void MapTo_Strict_IdentityNullNonNullableCollection_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Strict);
        var dest = new CollectionsDto { Lines = [new LineDto { Label = "existing" }] };

        Assert.Throws<InvalidOperationException>(
            () => mappers.Identity.MapTo(new ParentSource { Lines = null! }, dest));
    }

    [Fact]
    public void MapTo_Lenient_IdentityNullCollection_ClearsDestination()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);
        var dest = new CollectionsDto { Lines = [new LineDto { Label = "existing" }] };

        mappers.Identity.MapTo(new ParentSource { Lines = null! }, dest);

        Assert.Empty(dest.Lines);
    }

    // -----------------------------------------------------------------------
    // Configuration levels
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_StrictFromGlobalOptions_Throws()
    {
        var mappers = new GlobalOptionsMappers(new GlobalMapperOptions().SetNullHandling(NullHandling.Strict));

        Assert.Throws<InvalidOperationException>(() => mappers.Line.Map(new ParentSource { Lines = null! }));
    }

    [Fact]
    public void Map_LenientByDefault_MapsToEmpty()
    {
        var mappers = new GlobalOptionsMappers(null);

        Assert.Empty(mappers.Line.Map(new ParentSource { Lines = null! }).Lines);
    }

    [Fact]
    public void Map_StrictFromSetNullHandling_Throws()
    {
        var mappers = new NullHandlingMappers(NullHandling.Lenient);

        Assert.Throws<InvalidOperationException>(() => mappers.StrictOverride.Map(new ParentSource { Lines = null! }));
    }

    // -----------------------------------------------------------------------
    // LINQ projection is unaffected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Project_Strict_ParentWithoutChildren_TranslatesToEmptyAsync()
    {
        DbContextOptions<NullHandlingDbContext> options = new DbContextOptionsBuilder<NullHandlingDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        await using var db = new NullHandlingDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        db.Parents.AddRange(
            new ParentEntity { Id = 1, Lines = [new LineEntity { Id = 1, Label = "a" }] },
            new ParentEntity { Id = 2 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mappers = new NullHandlingMappers(NullHandling.Strict);
        List<EntityDto> results = await db.Parents
            .OrderBy(p => p.Id)
            .Project(mappers.Entity)
            .ToListAsync();

        Assert.Equal("a", Assert.Single(results[0].Lines).Label);
        Assert.Empty(results[1].Lines);
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

file sealed class ChildSource
{
    public string Name { get; set; } = "child";
}

file sealed class ChildDto
{
    public required string Name { get; set; }
}

file sealed class ParentSource
{
    public List<LineSource> Lines { get; set; } = [];
    public List<LineSource>? MaybeLines { get; set; }
    public ChildSource Child { get; set; } = new();
    public ChildSource? MaybeChild { get; set; }
}

file sealed class CollectionsDto
{
    public List<LineDto> Lines { get; set; } = [];
    public HashSet<string> Labels { get; set; } = [];
    public List<LineDto>? MaybeLines { get; set; }
}

file sealed class ObjectsDto
{
    public ChildDto? Child { get; set; }
    public ChildDto? MaybeChild { get; set; }
}

file sealed class HolderSource
{
    public required ParentSource Inner { get; set; }
}

file sealed class HolderDto
{
    public required CollectionsDto Inner { get; set; }
}

file sealed class EntityDto
{
    public List<LineDto> Lines { get; set; } = [];
}

internal sealed class ParentEntity
{
    public int Id { get; set; }
    public List<LineEntity> Lines { get; set; } = [];
}

internal sealed class LineEntity
{
    public int Id { get; set; }
    public required string Label { get; set; }
}

// ---------------------------------------------------------------------------
// Mappers
// ---------------------------------------------------------------------------

file sealed class NullHandlingMappers : MapperContext
{
    private readonly NullHandling _mode;

    public NullHandlingMappers(NullHandling mode)
    {
        _mode = mode;

        Line  = CreateMapper<LineSource, LineDto>(s => new LineDto { Label = s.Label });
        ChildMapper = CreateMapper<ChildSource, ChildDto>(s => new ChildDto { Name = s.Name });

        Collections = CreateMapper<ParentSource, CollectionsDto>(s => new CollectionsDto
        {
            Lines      = s.Lines.Project(Line).ToList(),
            Labels     = s.Lines.Select(l => l.Label).ToHashSet(),
            MaybeLines = s.MaybeLines!.Project(Line).ToList(),
        });

        Asserted = CreateMapper<ParentSource, CollectionsDto>(s => new CollectionsDto
        {
            Lines = s.MaybeLines!.Project(Line).ToList(),
        })
        .Ignore(d => d.Labels);

        Objects = CreateMapper<ParentSource, ObjectsDto>(s => new ObjectsDto
        {
            Child      = ChildMapper.Map(s.Child),
            MaybeChild = ChildMapper.Map(s.MaybeChild),
        });

        Holder = CreateMapper<HolderSource, HolderDto>(s => new HolderDto
        {
            Inner = Collections.Map(s.Inner),
        });

        Deep = CreateMapper<ParentSource, CollectionsDto>(s => new CollectionsDto
        {
            Lines = s.Lines.Project(Line).ToList(),
        })
        .Ignore(d => d.Labels)
        .MapToCollection<LineDto>(d => d.Lines, CollectionMapToMode.Deep);

        Identity = CreateMapper<ParentSource, CollectionsDto>(s => new CollectionsDto
        {
            Lines = s.Lines.Project(Line).ToList(),
        })
        .Ignore(d => d.Labels)
        .MapToCollection(d => d.Lines, s => s.Lines, Line, s => s.Label, d => d.Label);

        StrictOverride = CreateMapper<ParentSource, CollectionsDto>(s => new CollectionsDto
        {
            Lines = s.Lines.Project(Line).ToList(),
        })
        .Ignore(d => d.Labels)
        .SetNullHandling(NullHandling.Strict);

        Entity = CreateMapper<ParentEntity, EntityDto>(s => new EntityDto
        {
            Lines = s.Lines.Select(l => new LineDto { Label = l.Label }).ToList(),
        });
    }

    public Mapper<LineSource, LineDto> Line { get; }
    public Mapper<ChildSource, ChildDto> ChildMapper { get; }
    public Mapper<ParentSource, CollectionsDto> Collections { get; }
    public Mapper<ParentSource, CollectionsDto> Asserted { get; }
    public Mapper<ParentSource, ObjectsDto> Objects { get; }
    public Mapper<HolderSource, HolderDto> Holder { get; }
    public Mapper<ParentSource, CollectionsDto> Deep { get; }
    public Mapper<ParentSource, CollectionsDto> Identity { get; }
    public Mapper<ParentSource, CollectionsDto> StrictOverride { get; }
    public Mapper<ParentEntity, EntityDto> Entity { get; }

    protected override NullHandling DefaultNullHandling => _mode;
}

file sealed class GlobalOptionsMappers : MapperContext
{
    public GlobalOptionsMappers(GlobalMapperOptions? options)
        : base(options)
    {
        Item = CreateMapper<LineSource, LineDto>(s => new LineDto { Label = s.Label });
        Line = CreateMapper<ParentSource, CollectionsDto>(s => new CollectionsDto
        {
            Lines = s.Lines.Project(Item).ToList(),
        })
        .Ignore(d => d.Labels);
    }

    public Mapper<LineSource, LineDto> Item { get; }
    public Mapper<ParentSource, CollectionsDto> Line { get; }
}

internal sealed class NullHandlingDbContext(DbContextOptions<NullHandlingDbContext> options) : DbContext(options)
{
    public DbSet<ParentEntity> Parents => Set<ParentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParentEntity>().HasMany(p => p.Lines).WithOne().HasForeignKey("ParentId");
    }
}
