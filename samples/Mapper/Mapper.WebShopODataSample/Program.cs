using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using Spectre.Console;
using Mapper.WebShopODataSample.Data;
using Mapper.WebShopODataSample.Mappers;
using Mapper.WebShopODataSample.Projections;

// ---------------------------------------------------------------------------
// Mapper.WebShopODataSample
//
// Demonstrates ArchPillar.Extensions.Mapper behind an ASP.NET Core OData API
// (Controllers) over EF Core SQLite:
//   - MapperContext (WebShopMappers) holding every Mapper as a named property.
//   - Controllers calling Project() to expose projection DTOs as OData entity
//     sets — flattening (CategoryName), computed columns (IsAvailable), and
//     aggregates (TotalSpent, TotalOrders).
//   - [EnableQuery] composing $select / $filter / $orderby / $top / $count /
//     $expand on top of the mapper's IQueryable, so OData options translate all
//     the way to SQL rather than running in memory.
//   - Optional nested members (Order.Lines) materialised only when Include()
//     opts them in (the order-by-key route).
//
// Targets net8.0 because Microsoft.AspNetCore.OData 9.x does — a tech-demo
// constraint, not a framework recommendation.
//
// Domain types live under Models/, read DTOs under Projections/, the
// MapperContext under Mappers/, and OData controllers under Controllers/ —
// one file per class.
// ---------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────────────────────────

var sqliteConnectionString =
    builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=webshop-odata.db";

builder.Services.AddDbContext<WebShopDbContext>(options =>
{
    options.UseSqlite(sqliteConnectionString);
});

// ── OData EDM Model ─────────────────────────────────────────────────────────

ODataConventionModelBuilder edm = new();
edm.EntitySet<CategoryProjection>("Categories");
edm.EntitySet<ProductProjection>("Products");
edm.EntitySet<OrderProjection>("Orders");
edm.EntitySet<CustomerProjection>("Customers");

// ── OData + Controllers ─────────────────────────────────────────────────────

builder.Services.AddControllers()
    .AddOData(options =>
    {
        options.AddRouteComponents("odata", edm.GetEdmModel())
               .Select()
               .Filter()
               .OrderBy()
               .Count()
               .SetMaxTop(100)
               .Expand();
    });

// ── Application services ────────────────────────────────────────────────────

builder.Services.AddSingleton<WebShopMappers>();

// ── App ──────────────────────────────────────────────────────────────────────

WebApplication app = builder.Build();

// Ensure database schema is up to date on every start.
using (IServiceScope startupScope = app.Services.CreateScope())
{
    WebShopDbContext startupDb = startupScope.ServiceProvider.GetRequiredService<WebShopDbContext>();
    await startupDb.Database.EnsureCreatedAsync();
}

// Optional data seeding: dotnet run -- --seed
if (args.Contains("--seed"))
{
    using IServiceScope seedScope = app.Services.CreateScope();
    WebShopDbContext seedDb        = seedScope.ServiceProvider.GetRequiredService<WebShopDbContext>();
    ILogger<WebApplication> seedLogger = seedScope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();
    await Seeder.SeedAsync(seedDb, seedLogger);
}

app.UseHttpsRedirection();
app.UseRouting();
app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var rawAddress = app.Urls.FirstOrDefault(u => u.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                    ?? app.Urls.FirstOrDefault()
                    ?? "http://localhost:5000";
    var uri         = new Uri(rawAddress);
    var baseAddress = $"{uri.Scheme}://localhost:{uri.Port}";

    var odataUrl    = $"{baseAddress}/odata";
    var metadataUrl = $"{odataUrl}/$metadata";

    Grid grid = new Grid().AddColumn().AddColumn();
    grid.AddRow(new Markup("[grey]OData root[/]"),     new Text(odataUrl,    new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]OData metadata[/]"), new Text(metadataUrl, new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]Example[/]"),         new Text($"{odataUrl}/Products?$filter=Price gt 50&$orderby=Name&$top=10", new Style(Color.Grey)));

    AnsiConsole.Write(new Panel(grid)
    {
        Header  = new PanelHeader(" [bold yellow]WebShop OData API[/] "),
        Border  = BoxBorder.Rounded,
        Padding = new Padding(1, 0),
    });
});

app.Run();

/// <summary>Exposes the implicitly-defined <c>Program</c> class for integration testing via <c>WebApplicationFactory</c>.</summary>
public partial class Program;
