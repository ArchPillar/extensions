// Skill oracle runner for the `archpillar-mapper` Agent Skill.
// Purpose, methodology, and re-test modes: docs/mapper/internals/llm-skill-testing.md
using Microsoft.EntityFrameworkCore;
using MapTest;

var failed = false;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : $" — {detail}")}");
    if (!ok) { failed = true; }
}
void Build(string name, Action act)
{
    try { act(); Console.WriteLine($"  [PASS] {name} — EagerBuildAll() ok"); }
    catch (Exception ex) { Console.WriteLine($"  [FAIL] {name} — {ex.GetType().Name}: {ex.Message}"); failed = true; }
}

Console.WriteLine("== build-time validation (run EagerBuildAll) ==");
Build("Clean.AppMappers",             () => { _ = new MapTest.Clean.AppMappers(); });
Build("EnumScenario.PriorityMappers", () => { _ = new MapTest.EnumScenario.PriorityMappers(); });
Build("DictScenario.CatalogMappers",  () => { _ = new MapTest.DictScenario.CatalogMappers(); });
Build("MapToScenario.OrderMappers",   () => { _ = new MapTest.MapToScenario.OrderMappers(); });

Console.WriteLine("\n== behavior: mappers actually map ==");

// Enum (many-to-one): the collapse must be correct, not just buildable.
var pm = new MapTest.EnumScenario.PriorityMappers();
Check("Enum Low→Normal",      pm.PriorityBucket.Map(MapTest.EnumScenario.PriorityX.Low)      == MapTest.EnumScenario.PriorityBucketDtoX.Normal);
Check("Enum Medium→Normal",   pm.PriorityBucket.Map(MapTest.EnumScenario.PriorityX.Medium)   == MapTest.EnumScenario.PriorityBucketDtoX.Normal);
Check("Enum High→Urgent",     pm.PriorityBucket.Map(MapTest.EnumScenario.PriorityX.High)     == MapTest.EnumScenario.PriorityBucketDtoX.Urgent);
Check("Enum Critical→Urgent", pm.PriorityBucket.Map(MapTest.EnumScenario.PriorityX.Critical) == MapTest.EnumScenario.PriorityBucketDtoX.Urgent);

// Dictionary: keys preserved, values mapped through the Item mapper.
var cm = new MapTest.DictScenario.CatalogMappers();
var catalog = new MapTest.DictScenario.Catalog
{
    Id = 7,
    Items = { ["apple"] = new MapTest.DictScenario.Item { Sku = "A1", Name = "Apple" } },
};
var catalogDto = cm.Catalog.Map(catalog)!;
Check("Dict id mapped",    catalogDto.Id == 7);
Check("Dict key preserved", catalogDto.Items.ContainsKey("apple"));
Check("Dict value mapped",  catalogDto.Items.TryGetValue("apple", out var item) && item.Sku == "A1" && item.Name == "Apple");

// MapTo DeepWithIdentity: collection instance + matched element instances preserved,
// non-matches added/removed, scalars updated.
var om = new MapTest.MapToScenario.OrderMappers();
var tracked = new MapTest.MapToScenario.OrderEntity
{
    Id = 1, Notes = "old",
    Lines =
    {
        new MapTest.MapToScenario.LineEntity { Id = 1, Quantity = 1, UnitPrice = 10m },
        new MapTest.MapToScenario.LineEntity { Id = 2, Quantity = 2, UnitPrice = 20m },
    },
};
var originalCollection = tracked.Lines;
var originalLine1 = tracked.Lines.Single(l => l.Id == 1);
var command = new MapTest.MapToScenario.OrderUpdateCommand
{
    Id = 1, Notes = "new",
    Lines =
    {
        new MapTest.MapToScenario.LineUpdateCommand { Id = 1, Quantity = 99, UnitPrice = 10m }, // update
        new MapTest.MapToScenario.LineUpdateCommand { Id = 3, Quantity = 3,  UnitPrice = 30m }, // add
        // Id 2 omitted → should be removed
    },
};
MapTest.MapToScenario.OrderUpdater.Apply(om, tracked, command);
Check("MapTo scalar updated",       tracked.Notes == "new");
Check("MapTo collection preserved", ReferenceEquals(tracked.Lines, originalCollection));
Check("MapTo matched instance kept", tracked.Lines.Any(l => ReferenceEquals(l, originalLine1)) && originalLine1.Quantity == 99);
Check("MapTo unmatched removed",    tracked.Lines.All(l => l.Id != 2));
Check("MapTo new item added",       tracked.Lines.Any(l => l.Id == 3));
Check("MapTo final count",          tracked.Lines.Count == 2);

Console.WriteLine("\n== SQL translation + values (SQLite) — Clean Order projection ==");
try
{
    var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
    conn.Open();
    var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
    using var db = new AppDbContext(opts);
    db.Database.EnsureCreated();
    db.Orders.Add(new Order
    {
        Id = 1, Status = OrderStatus.Shipped, Priority = Priority.High, OwnerId = 42,
        IsActive = true, Customer = new Customer { Name = "Acme" },
        Lines = { new OrderLine { Id = 1, Quantity = 2, UnitPrice = 9.99m, Product = new Product { Name = "Widget" } } },
    });
    db.SaveChanges();

    var rows = MapTest.Clean.CleanQuery.GetActiveOrdersAsync(db, new MapTest.Clean.AppMappers(), 42).GetAwaiter().GetResult();
    var dto = rows.Single();
    Check("Clean row count",   rows.Count == 1);
    Check("Clean Id",          dto.Id == 1);
    Check("Clean enum→SQL",    dto.Status == OrderStatusDto.Shipped);
    Check("Clean variable",    dto.IsOwner);                      // OwnerId 42 == currentUserId 42
    Check("Clean optional join", dto.CustomerName == "Acme");
    Check("Clean nested line",  dto.Lines.Count == 1 && dto.Lines[0].Quantity == 2);
}
catch (Exception ex) { Console.WriteLine($"  [FAIL] SQL translation — {ex.GetType().Name}: {ex.Message}"); failed = true; }

Console.WriteLine($"\n{(failed ? "ORACLE FAILED" : "ORACLE PASSED")}");
return failed ? 1 : 0;
