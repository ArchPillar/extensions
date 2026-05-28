using ArchPillar.Extensions.EntityFrameworkCore.Npgsql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// ---------------------------------------------------------------------------
// Npgsql.Sample
//
// Demonstrates the four Tier 1 features of
// ArchPillar.Extensions.EntityFrameworkCore.Npgsql against a live PostgreSQL
// instance:
//
//   1. Guid '…'::uuid literal cast — Guid constants can be projected as read
//      columns without "field is text" InvalidCastException.
//   2. DateTimeOffset → timestamptz, always stored as UTC.
//   3. DateTime → timestamptz, UTC-enforcing (rejects Unspecified).
//   4. CLR enum ↔ int4 on the wire.
//   5. EF.Functions.JsonbBuildObject(...) → jsonb_build_object(...).
//
// Connection: set NPGSQL_SAMPLE_CONNECTION_STRING or run a local PostgreSQL at
//             Host=localhost;Port=5432;Username=app;Password=postgres
// ---------------------------------------------------------------------------

var connectionString =
    Environment.GetEnvironmentVariable("NPGSQL_SAMPLE_CONNECTION_STRING")
    ?? "Host=localhost;Port=5432;Username=app;Password=postgres;Database=archpillar_npgsql_sample";

// 1. Build a data source with the ADO-wire converters installed.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseArchPillarNpgsqlImprovements();
await using var dataSource = dataSourceBuilder.Build();

// 2. Register the EF-side integration (Guid literal cast + jsonb helpers).
var options = new DbContextOptionsBuilder<TicketContext>()
    .UseNpgsql(dataSource)
    .UseArchPillarNpgsqlImprovements()
    .Options;

await using var ctx = new TicketContext(options);
await ctx.Database.EnsureDeletedAsync();
await ctx.Database.EnsureCreatedAsync();

// Seed a couple of rows.
ctx.Tickets.AddRange(
    new Ticket
    {
        Id = Guid.NewGuid(),
        Title = "outage in EU",
        OpenedAt = DateTime.UtcNow,
        OccurredAt = new DateTimeOffset(DateTime.UtcNow.AddHours(-2).Ticks, TimeSpan.Zero),
        Severity = Severity.Critical,
    },
    new Ticket
    {
        Id = Guid.NewGuid(),
        Title = "minor UI glitch",
        OpenedAt = DateTime.UtcNow,
        OccurredAt = DateTimeOffset.UtcNow,
        Severity = Severity.Low,
    });
await ctx.SaveChangesAsync();
ctx.ChangeTracker.Clear();

// ---------------------------------------------------------------------------
// Feature 1 — Guid constant projected as a read column.
// Without ::uuid the constant comes back as text and the reader throws.
// ---------------------------------------------------------------------------
var stamp = Guid.Parse("57afda40-0000-0000-0000-000000000000");
var byTicket = await ctx.Tickets
    .Select(t => new { t.Id, RequestStamp = stamp })
    .ToListAsync();

Console.WriteLine("== Guid literal cast ==");
foreach (var row in byTicket)
{
    Console.WriteLine($"  ticket={row.Id:N} stamp={row.RequestStamp:N}");
}

// ---------------------------------------------------------------------------
// Feature 2/3 — DateTime / DateTimeOffset always come back as UTC.
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== UTC date/time ==");
foreach (var t in await ctx.Tickets.ToListAsync())
{
    Console.WriteLine($"  {t.Title}: opened={t.OpenedAt:O} (Kind={t.OpenedAt.Kind}), occurred={t.OccurredAt:O}");
}

// ---------------------------------------------------------------------------
// Feature 4 — enum stored as int4.
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== enum<->int4 ==");
var raw = await ctx.Tickets.Select(t => new { t.Title, Severity = (int)t.Severity }).ToListAsync();
foreach (var row in raw)
{
    Console.WriteLine($"  {row.Title}: severity={row.Severity}");
}

// ---------------------------------------------------------------------------
// Feature 5 — EF.Functions.JsonbBuildObject inside a query.
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== jsonb_build_object (fixed-arity) ==");
var jsons = await ctx.Tickets
    .Select(t => EF.Functions.JsonbBuildObject(
        "id", t.Id,
        "title", t.Title,
        "severity", (int)t.Severity))
    .ToListAsync();
foreach (var json in jsons)
{
    Console.WriteLine($"  {json}");
}

Console.WriteLine();
Console.WriteLine("== jsonb_build_object (fluent builder) ==");
var builtJsons = await ctx.Tickets
    .Select(t => EF.Functions.JsonbObject("id", t.Id)
        .Add("title", t.Title)
        .Add("severity", (int)t.Severity)
        .Add("openedAt", t.OpenedAt)
        .Build())
    .ToListAsync();
foreach (var json in builtJsons)
{
    Console.WriteLine($"  {json}");
}

await ctx.Database.EnsureDeletedAsync();

// ---------------------------------------------------------------------------
// Model
// ---------------------------------------------------------------------------

internal enum Severity
{
    Low = 1,
    Medium = 5,
    High = 9,
    Critical = 99,
}

internal sealed class Ticket
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Severity Severity { get; set; }
}

internal sealed class TicketContext(DbContextOptions<TicketContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(e =>
        {
            e.ToTable("sample_tickets");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnType("uuid");
            e.Property(t => t.Title).HasMaxLength(200);
            e.Property(t => t.OpenedAt).HasColumnType("timestamptz");
            e.Property(t => t.OccurredAt).HasColumnType("timestamptz");
            e.Property(t => t.Severity).HasColumnType("integer");
        });
    }
}
