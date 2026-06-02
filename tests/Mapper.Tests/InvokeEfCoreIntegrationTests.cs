using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

// Source entities: Agent → Form → FormData (a 3-level navigation chain).
public sealed class IxAgent { public int Id { get; set; } public IxForm Form { get; set; } = null!; }
public sealed class IxForm { public int Id { get; set; } public string Name { get; set; } = ""; public IxFormData Data { get; set; } = null!; }
public sealed class IxFormData { public int Id { get; set; } public string Raw { get; set; } = ""; }

public sealed class IxAgentVm { public int Id { get; set; } public IxFormVm? Form { get; set; } }
public sealed class IxFormVm { public int Id { get; set; } public string Name { get; set; } = ""; public IxFormDataVm? Data { get; set; } }
public sealed class IxFormDataVm { public int Id { get; set; } public string Value { get; set; } = ""; }

/// <summary>
/// Mappers accessed through their context (the recommended pattern). The
/// <see cref="FormData"/> mapping routes through an <em>instance</em> method,
/// so it cannot be translated to SQL — it must run in memory. <see cref="Form"/>
/// reaches it via <c>FormData.Invoke(...)</c>.
/// </summary>
public sealed class IxMappers : MapperContext
{
    public Mapper<IxFormData, IxFormDataVm> FormData { get; }
    public Mapper<IxForm, IxFormVm> Form { get; }            // FormData via Invoke (client-side)
    public Mapper<IxForm, IxFormVm> FormInlined { get; }     // FormData via Map (would be translated → fails)
    public Mapper<IxAgent, IxAgentVm> Agent { get; }         // inlines Form one level deeper

    // Instance method (captures the context) doing non-translatable work.
    private IxFormDataVm BuildData(IxFormData d)
        => new() { Id = d.Id, Value = new string(d.Raw.Reverse().ToArray()) };

    public IxMappers()
    {
        FormData = CreateMapper<IxFormData, IxFormDataVm>(src => new IxFormDataVm
        {
            Id    = src.Id,
            Value = BuildData(src).Value,
        });

        Form = CreateMapper<IxForm, IxFormVm>(src => new IxFormVm
        {
            Id   = src.Id,
            Name = src.Name,
            Data = FormData.Invoke(src.Data),
        });

        FormInlined = CreateMapper<IxForm, IxFormVm>(src => new IxFormVm
        {
            Id   = src.Id,
            Name = src.Name,
            Data = FormData.Map(src.Data),
        });

        Agent = CreateMapper<IxAgent, IxAgentVm>(src => new IxAgentVm
        {
            Id   = src.Id,
            Form = Form.Map(src.Form),
        });
    }
}

internal sealed class IxDbContext(DbContextOptions<IxDbContext> options) : DbContext(options)
{
    public DbSet<IxAgent> Agents => Set<IxAgent>();
    public DbSet<IxForm> Forms => Set<IxForm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IxAgent>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne(a => a.Form).WithOne().HasForeignKey<IxForm>("AgentId");
        });
        modelBuilder.Entity<IxForm>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasOne(f => f.Data).WithOne().HasForeignKey<IxFormData>("FormId");
        });
        modelBuilder.Entity<IxFormData>(e => e.HasKey(d => d.Id));
    }
}

/// <summary>
/// Verifies <c>Invoke</c> works under <see cref="MapperDbContextOptionsExtensions.UseArchPillarMapper"/>
/// — the configuration the original PR's tests never exercised. Uses SQLite so
/// the full relational client-projection verifier runs.
/// </summary>
public sealed class InvokeEfCoreIntegrationTests : IDisposable
{
    private readonly IxDbContext _db;
    private readonly IxMappers _mappers = new();

    public InvokeEfCoreIntegrationTests()
    {
        DbContextOptionsBuilder<IxDbContext> builder = new DbContextOptionsBuilder<IxDbContext>()
            .UseSqlite("DataSource=:memory:");
        builder.UseArchPillarMapper();
        _db = new IxDbContext(builder.Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _db.Agents.Add(new IxAgent
        {
            Id   = 1,
            Form = new IxForm { Id = 1, Name = "F1", Data = new IxFormData { Id = 1, Raw = "hello" } },
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task Invoke_Projection_RunsNestedMapperInMemoryAsync()
    {
        IxFormVm result = await _db.Forms
            .Where(f => f.Id == 1)
            .Project(_mappers.Form)
            .SingleAsync();

        Assert.Equal(1, result.Id);
        Assert.NotNull(result.Data);
        Assert.Equal("olleh", result.Data.Value);   // instance method ran client-side
    }

    [Fact]
    public async Task Invoke_NestedOneLevelDeeper_RunsInMemoryAsync()
    {
        IxAgentVm result = await _db.Agents
            .Where(a => a.Id == 1)
            .Project(_mappers.Agent)
            .SingleAsync();

        Assert.NotNull(result.Form);
        Assert.NotNull(result.Form.Data);
        Assert.Equal("olleh", result.Form.Data.Value);
    }

    [Fact]
    public async Task Map_InlineNonTranslatable_ThrowsAsync()
    {
        // Documents why Invoke is needed: inlining the non-translatable mapping
        // captures the context constant, which EF rejects.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Forms
                .Where(f => f.Id == 1)
                .Project(_mappers.FormInlined)
                .SingleAsync());
    }
}
