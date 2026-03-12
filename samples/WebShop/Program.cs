using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenIddict.Validation.AspNetCore;
using Scalar.AspNetCore;
using Spectre.Console;
using WebShop.Data;
using WebShop.Endpoints;
using WebShop.Mappers;
using WebShop.Models;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────────────────────────

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");

if (postgresConnectionString is not null)
{
    builder.Services.AddDbContext<WebShopDbContext>(options =>
    {
        options.UseNpgsql(postgresConnectionString);
        options.UseOpenIddict();
    });
}
else
{
    var sqliteConnectionString =
        builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=webshop.db";

    builder.Services.AddDbContext<WebShopDbContext>(options =>
    {
        options.UseSqlite(sqliteConnectionString);
        options.UseOpenIddict();
    });
}

// ── Identity ────────────────────────────────────────────────────────────────

builder.Services
    .AddIdentityCore<WebShopUser>(options =>
    {
        options.Password.RequireDigit           = true;
        options.Password.RequiredLength         = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase       = false;
        options.User.RequireUniqueEmail         = true;
    })
    .AddEntityFrameworkStores<WebShopDbContext>();

// ── OpenIddict ───────────────────────────────────────────────────────────────

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<WebShopDbContext>();
    })
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowPasswordFlow()
               .AllowRefreshTokenFlow();
        options.AcceptAnonymousClients();

        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// ── Authentication & Authorisation ──────────────────────────────────────────

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("role", "Admin"));
});

// ── OpenAPI / Scalar ─────────────────────────────────────────────────────────

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title       = "WebShop API",
            Version     = "v1",
            Description = "Headless webshop sample — powered by ArchPillar.Mapper.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            Description  = "JWT access token. Obtain one via POST /connect/token.",
        };

        return Task.CompletedTask;
    });
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
    using IServiceScope seedScope        = app.Services.CreateScope();
    WebShopDbContext seedDb              = seedScope.ServiceProvider.GetRequiredService<WebShopDbContext>();
    UserManager<WebShopUser> userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<WebShopUser>>();
    ILogger<WebApplication> seedLogger  = seedScope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();
    await Seeder.SeedAsync(seedDb, userManager, seedLogger);
}

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("WebShop API")
    .AddPreferredSecuritySchemes("Bearer")
    .AddHttpAuthentication("Bearer", _ => { }));

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ────────────────────────────────────────────────────────────────

app.MapAuthEndpoints();
app.MapCategoryEndpoints();
app.MapProductEndpoints();
app.MapOrderEndpoints();
app.MapCustomerEndpoints();
app.MapUserEndpoints();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var rawAddress   = app.Urls.FirstOrDefault(u => u.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                    ?? app.Urls.FirstOrDefault()
                    ?? "http://localhost:5000";
    var uri          = new Uri(rawAddress);
    var httpsAddress = $"{uri.Scheme}://webshop.dev.localhost:{uri.Port}";

    var scalarUrl = $"{httpsAddress}/scalar/v1";
    var openApiUrl = $"{httpsAddress}/openapi/v1.json";
    var tokenUrl = $"{httpsAddress}/connect/token";

    Grid grid = new Grid().AddColumn().AddColumn();
    grid.AddRow(new Markup("[grey]Scalar UI[/]"),   new Text(scalarUrl,  new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]OpenAPI[/]"),     new Text(openApiUrl, new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]Token URL[/]"),   new Text(tokenUrl,   new Style(Color.Aqua)));
    grid.AddRow(new Markup("[grey]grant_type[/]"),  new Markup("[dim]password[/]"));
    grid.AddRow(new Markup("[grey]scopes[/]"),      new Markup("[dim](none)[/]"));
    grid.AddRow(new Markup("[grey]username[/]"),    new Markup("[dim]admin@webshop.example[/]"));
    grid.AddRow(new Markup("[grey]password[/]"),    new Markup("[dim]Admin123![/]"));

    AnsiConsole.Write(new Panel(grid)
    {
        Header  = new PanelHeader(" [bold yellow]WebShop API[/] "),
        Border  = BoxBorder.Rounded,
        Padding = new Padding(1, 0),
    });
});

app.Run();
