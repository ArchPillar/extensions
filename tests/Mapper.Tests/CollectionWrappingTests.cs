namespace ArchPillar.Mapper.Tests;

/// <summary>
/// Verifies that collection destination properties are wrapped with the
/// correct materialisation method (ToList, ToArray, ToHashSet) based on
/// the destination property type.
/// </summary>
public class CollectionWrappingTests
{
    [Fact]
    public void Map_CollectionToList_ProducesListResult()
    {
        var mappers = new CollectionMapperContext();
        var src = new CollectionSource { Items = [new Item { Name = "A" }, new Item { Name = "B" }] };

        ListDest? dto = mappers.ToListMapper.Map(src);

        Assert.IsType<List<ItemDto>>(dto!.Items);
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal("A", dto.Items[0].Name);
    }

    [Fact]
    public void Map_CollectionToArray_ProducesArrayResult()
    {
        var mappers = new CollectionMapperContext();
        var src = new CollectionSource { Items = [new Item { Name = "X" }, new Item { Name = "Y" }] };

        ArrayDest? dto = mappers.ToArrayMapper.Map(src);

        Assert.IsType<ItemDto[]>(dto!.Items);
        Assert.Equal(2, dto.Items.Length);
        Assert.Equal("X", dto.Items[0].Name);
    }

    [Fact]
    public void Map_CollectionToHashSet_ProducesHashSetResult()
    {
        var mappers = new CollectionMapperContext();
        var src = new CollectionSource { Items = [new Item { Name = "P" }, new Item { Name = "Q" }] };

        HashSetDest? dto = mappers.ToHashSetMapper.Map(src);

        Assert.IsType<HashSet<ItemDto>>(dto!.Items);
        Assert.Equal(2, dto.Items.Count);
    }

    [Fact]
    public void Project_CollectionToList_ProducesListResult()
    {
        var mappers = new CollectionMapperContext();
        IQueryable<CollectionSource> sources = new[] { new CollectionSource { Items = [new Item { Name = "A" }] } }.AsQueryable();

        var results = sources.Select(mappers.ToListMapper.ToExpression()).ToList();

        Assert.IsType<List<ItemDto>>(results[0].Items);
        Assert.Single(results[0].Items);
    }

    [Fact]
    public void Project_CollectionToArray_ProducesArrayResult()
    {
        var mappers = new CollectionMapperContext();
        IQueryable<CollectionSource> sources = new[] { new CollectionSource { Items = [new Item { Name = "A" }] } }.AsQueryable();

        var results = sources.Select(mappers.ToArrayMapper.ToExpression()).ToList();

        Assert.IsType<ItemDto[]>(results[0].Items);
        Assert.Single(results[0].Items);
    }

    [Fact]
    public void Project_CollectionToHashSet_ProducesHashSetResult()
    {
        var mappers = new CollectionMapperContext();
        IQueryable<CollectionSource> sources = new[] { new CollectionSource { Items = [new Item { Name = "A" }] } }.AsQueryable();

        var results = sources.Select(mappers.ToHashSetMapper.ToExpression()).ToList();

        Assert.IsType<HashSet<ItemDto>>(results[0].Items);
        Assert.Single(results[0].Items);
    }
}

// ---------------------------------------------------------------------------
// Test-local models and mappers
// ---------------------------------------------------------------------------

file class Item
{
    public required string Name { get; set; }
}

file class ItemDto
{
    public required string Name { get; set; }
}

file class CollectionSource
{
    public List<Item> Items { get; set; } = [];
}

file class ListDest
{
    public required List<ItemDto> Items { get; set; }
}

file class ArrayDest
{
    public required ItemDto[] Items { get; set; }
}

file class HashSetDest
{
    public required HashSet<ItemDto> Items { get; set; }
}

file class CollectionMapperContext : MapperContext
{
    public Mapper<Item, ItemDto> Item { get; }
    public Mapper<CollectionSource, ListDest> ToListMapper { get; }
    public Mapper<CollectionSource, ArrayDest> ToArrayMapper { get; }
    public Mapper<CollectionSource, HashSetDest> ToHashSetMapper { get; }

    public CollectionMapperContext()
    {
        Item = CreateMapper<Item, ItemDto>(s => new ItemDto { Name = s.Name });

        ToListMapper = CreateMapper<CollectionSource, ListDest>(s => new ListDest
        {
            Items = s.Items.Project(Item).ToList(),
        });

        ToArrayMapper = CreateMapper<CollectionSource, ArrayDest>(s => new ArrayDest
        {
            Items = s.Items.Project(Item).ToArray(),
        });

        ToHashSetMapper = CreateMapper<CollectionSource, HashSetDest>(s => new HashSetDest
        {
            Items = s.Items.Project(Item).ToHashSet(),
        });
    }
}
