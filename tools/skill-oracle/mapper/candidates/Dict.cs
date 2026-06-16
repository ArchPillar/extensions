// SKILL-GENERATED (archpillar-mapper). Scenario: dictionary projection — map a
// Dictionary<string,Item> to Dictionary<string,ItemDto> preserving keys, translatable in EF Core.
using ArchPillar.Extensions.Mapper;
using Microsoft.EntityFrameworkCore;
namespace MapTest.DictScenario;

public class Item    { public string Sku { get; set; } = ""; public string Name { get; set; } = ""; }
public class ItemDto { public required string Sku { get; set; } public required string Name { get; set; } }
public class Catalog    { public int Id { get; set; } public Dictionary<string, Item> Items { get; set; } = new(); }
public class CatalogDto { public required int Id { get; set; } public required Dictionary<string, ItemDto> Items { get; set; } }

public class CatalogMappers : MapperContext
{
    public Mapper<Item, ItemDto> Item { get; }
    public Mapper<Catalog, CatalogDto> Catalog { get; }
    public CatalogMappers()
    {
        Item = CreateMapper<Item, ItemDto>(src => new ItemDto { Sku = src.Sku, Name = src.Name });
        Catalog = CreateMapper<Catalog, CatalogDto>(src => new CatalogDto
        {
            Id    = src.Id,
            Items = src.Items.ToDictionary(kvp => kvp.Key, kvp => Item.Map(kvp.Value)),
        });
        EagerBuildAll();
    }
}

public static class CatalogProjectionExample
{
    public static async Task<List<CatalogDto>> GetCatalogsAsync(DbContext db, CatalogMappers mappers)
        => await db.Set<Catalog>().Project(mappers.Catalog).ToListAsync();
}
