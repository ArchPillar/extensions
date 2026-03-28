namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Verifies in-memory mapping behaviour when nullable source properties
/// (optional scalar, nested mapper, nested collection) are all null.
/// </summary>
public class NullSourceMappingTests
{
    [Fact]
    public void Map_AllNullableSourcePropertiesNull_MapsWithoutError()
    {
        var mappers = new NullSourceMappers();
        var source = new NullableSource
        {
            Id = 1,
            Tag = null,
            Child = null,
            Items = null,
        };

        NullableDest? dest = mappers.Root.Map(source);

        Assert.NotNull(dest);
        Assert.Equal(1, dest.Id);
        Assert.Null(dest.Tag);
        Assert.Null(dest.Child);
        Assert.Null(dest.Items);
    }
}

// ---------------------------------------------------------------------------
// Test-local models and mappers
// ---------------------------------------------------------------------------

file class ChildSource
{
    public required string Name { get; set; }
}

file class ChildDest
{
    public required string Name { get; set; }
}

file class ItemSource
{
    public required string Label { get; set; }
}

file class ItemDest
{
    public required string Label { get; set; }
}

file class NullableSource
{
    public required int Id { get; set; }
    public string? Tag { get; set; }
    public ChildSource? Child { get; set; }
    public List<ItemSource>? Items { get; set; }
}

file class NullableDest
{
    public required int Id { get; set; }
    public string? Tag { get; set; }
    public ChildDest? Child { get; set; }
    public List<ItemDest>? Items { get; set; }
}

file class NullSourceMappers : MapperContext
{
    public Mapper<ChildSource, ChildDest> Child { get; }
    public Mapper<ItemSource, ItemDest> Item { get; }
    public Mapper<NullableSource, NullableDest> Root { get; }

    public NullSourceMappers()
    {
        Child = CreateMapper<ChildSource, ChildDest>(s => new ChildDest
        {
            Name = s.Name,
        });

        Item = CreateMapper<ItemSource, ItemDest>(s => new ItemDest
        {
            Label = s.Label,
        });

        Root = CreateMapper<NullableSource, NullableDest>(s => new NullableDest
        {
            Id = s.Id,
        })
        .Optional(d => d.Tag, s => s.Tag)
        .Optional(d => d.Child, s => Child.Map(s.Child))
        .Optional(d => d.Items, s => s.Items!.Project(Item).ToList());
    }
}
