// Skill oracle runner for the `archpillar-mapper` Agent Skill.
// Purpose, methodology, and re-test modes: docs/mapper/internals/llm-skill-testing.md
using Microsoft.EntityFrameworkCore;
using MapTest;

static void Build(string name, Action act)
{
    try { act(); Console.WriteLine($"  [PASS] {name} — EagerBuildAll() ok"); }
    catch (Exception ex) { Console.WriteLine($"  [FAIL] {name} — {ex.GetType().Name}: {ex.Message}"); }
}

Console.WriteLine("== build-time validation (run EagerBuildAll) ==");
Build("Clean.AppMappers",             () => { _ = new MapTest.Clean.AppMappers(); });
Build("EnumScenario.PriorityMappers", () => { _ = new MapTest.EnumScenario.PriorityMappers(); });
Build("DictScenario.CatalogMappers",  () => { _ = new MapTest.DictScenario.CatalogMappers(); });
Build("MapToScenario.OrderMappers",   () => { _ = new MapTest.MapToScenario.OrderMappers(); });

Console.WriteLine("\n== SQL translation (SQLite) — Clean Order projection ==");
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
    var dto = rows[0];
    Console.WriteLine($"  [PASS] translated+ran. rows={rows.Count} Status={dto.Status} IsOwner={dto.IsOwner} Customer={dto.CustomerName} Lines={dto.Lines.Count}");
}
catch (Exception ex) { Console.WriteLine($"  [FAIL] {ex.GetType().Name}: {ex.Message}"); }
