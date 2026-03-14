using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using Scalar.AspNetCore;
using Spectre.Console;
using WebShop.OData.Data;
using WebShop.OData.Mappers;
using WebShop.OData.Projections;

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

// ── OpenAPI / Scalar ─────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

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

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("WebShop OData API"));

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

    var scalarUrl  = $"{baseAddress}/scalar/v1";
    var openApiUrl = $"{baseAddress}/openapi/v1.json";
    var odataUrl   = $"{baseAddress}/odata";

    Grid grid = new Grid().AddColumn().AddColumn();
    grid.AddRow(new Markup("[grey]Scalar UI[/]"),   new Text(scalarUrl,  new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]OpenAPI[/]"),      new Text(openApiUrl, new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]OData root[/]"),   new Text(odataUrl,   new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]Example[/]"),      new Text($"{odataUrl}/Products?$filter=Price gt 50&$orderby=Name&$top=10", new Style(Color.Grey)));

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
