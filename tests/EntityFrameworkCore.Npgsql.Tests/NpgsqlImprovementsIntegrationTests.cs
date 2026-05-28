using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

[Collection("PostgreSQL")]
public sealed class NpgsqlImprovementsIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresTestDatabase.CreateAsync(fixture);
    }

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    [Fact]
    public async Task GuidConstant_ProjectedAsReadColumn_RoundTripsAsGuidAsync()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            var id = Guid.NewGuid();
            ctx.Rows.Add(NewRow(id));
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            // Project a Guid constant as a read column. Without the ::uuid cast,
            // PG types the literal as text and the reader throws InvalidCastException.
            var staticId = Guid.Parse("57afda40-0000-0000-0000-000000000000");
            var rows = await ctx.Rows
                .Select(r => new { Real = r.Id, Constant = staticId })
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(id, rows[0].Real);
            Assert.Equal(staticId, rows[0].Constant);
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public async Task DateTimeOffset_DifferentOffsets_StoreAsSameUtcInstantAsync()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            var instant = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var inUtc = new DateTimeOffset(instant, TimeSpan.Zero);
            var localTime = DateTime.SpecifyKind(instant.AddHours(2), DateTimeKind.Unspecified);
            var inPlusTwo = new DateTimeOffset(localTime, TimeSpan.FromHours(2));

            ctx.Rows.Add(NewRow(Guid.NewGuid(), occurredAt: inUtc));
            ctx.Rows.Add(NewRow(Guid.NewGuid(), occurredAt: inPlusTwo));
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            List<DateTimeOffset> stored = await ctx.Rows.OrderBy(r => r.OccurredAt).Select(r => r.OccurredAt).ToListAsync();
            Assert.Equal(2, stored.Count);
            Assert.Equal(inUtc.UtcDateTime, stored[0].UtcDateTime);
            Assert.Equal(inUtc.UtcDateTime, stored[1].UtcDateTime);
            Assert.All(stored, o => Assert.Equal(TimeSpan.Zero, o.Offset));
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public async Task DateTime_Utc_RoundTrips_AsUtcAsync()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            var utc = new DateTime(2024, 3, 15, 9, 30, 0, DateTimeKind.Utc);
            ctx.Rows.Add(NewRow(Guid.NewGuid(), createdAt: utc));
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            DateTime read = await ctx.Rows.Select(r => r.CreatedAt).SingleAsync();
            Assert.Equal(DateTimeKind.Utc, read.Kind);
            Assert.Equal(utc, read);
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public async Task DateTime_Unspecified_ThrowsAsync()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            var bad = new DateTime(2024, 3, 15, 9, 30, 0, DateTimeKind.Unspecified);
            ctx.Rows.Add(NewRow(Guid.NewGuid(), createdAt: bad));

            await Assert.ThrowsAnyAsync<Exception>(() => ctx.SaveChangesAsync());
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public async Task Enum_RoundTrips_AsInt4Async()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Rows.Add(NewRow(Guid.NewGuid(), priority: TestPriority.Critical));
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            TestPriority p = await ctx.Rows.Select(r => r.Priority).SingleAsync();
            Assert.Equal(TestPriority.Critical, p);

            // Confirm the column is stored as int4 by reading the raw value.
            var raw = await ctx.Rows.Select(r => (int)r.Priority).SingleAsync();
            Assert.Equal(99, raw);
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public async Task ToJsonb_AnonymousShape_TranslatesAndReturnsJsonStringAsync()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Rows.Add(NewRow(Guid.NewGuid(), name: "alice", priority: TestPriority.High));
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            var json = await ctx.Rows
                .Select(r => EF.Functions.ToJsonb(new
                {
                    name = r.Name,
                    priority = (int)r.Priority,
                    id = r.Id,
                    created = r.CreatedAt,
                }))
                .SingleAsync();

            Assert.Contains("\"name\"", json, StringComparison.Ordinal);
            Assert.Contains("alice", json, StringComparison.Ordinal);
            Assert.Contains("\"priority\"", json, StringComparison.Ordinal);
            Assert.Contains("9", json, StringComparison.Ordinal);
            Assert.Contains("\"id\"", json, StringComparison.Ordinal);
            Assert.Contains("\"created\"", json, StringComparison.Ordinal);
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public async Task ToJsonb_NamedClassShape_TranslatesAsync()
    {
        (NpgsqlDataSource ds, TestDbContext ctx) = TestContextFactory.Create(_database.ConnectionString);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Rows.Add(NewRow(Guid.NewGuid(), name: "bob"));
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            // A named type with an object initializer works the same as an anonymous type.
            var json = await ctx.Rows
                .Select(r => EF.Functions.ToJsonb(new JsonShape
                {
                    Name = r.Name,
                    Priority = (int)r.Priority,
                }))
                .SingleAsync();

            Assert.Contains("\"Name\"", json, StringComparison.Ordinal);
            Assert.Contains("bob", json, StringComparison.Ordinal);
            Assert.Contains("\"Priority\"", json, StringComparison.Ordinal);
        }
        finally
        {
            await ctx.DisposeAsync();
            await ds.DisposeAsync();
        }
    }

    [Fact]
    public void ToJsonb_InvokedDirectly_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => EF.Functions.ToJsonb(new { k = "v" }));
    }

    private sealed class JsonShape
    {
        public string Name { get; set; } = "";

        public int Priority { get; set; }
    }

    private static TestRow NewRow(
        Guid id,
        string name = "row",
        DateTime? createdAt = null,
        DateTimeOffset? occurredAt = null,
        TestPriority priority = TestPriority.Normal)
        => new()
        {
            Id = id,
            Name = name,
            CreatedAt = createdAt ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            OccurredAt = occurredAt ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Priority = priority,
        };
}
