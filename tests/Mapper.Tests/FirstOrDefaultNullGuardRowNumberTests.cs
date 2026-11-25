using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Models.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Reproduces #104 against PostgreSQL. A nested <c>Map()</c> over a
/// <c>FirstOrDefault()</c> of a filtered collection is null-guarded by comparing
/// the entity to null. When the target entity owns types (<c>OwnsOne</c>), EF Core
/// does not rewrite that comparison into <c>NOT EXISTS</c>; it projects the whole
/// entity through a <c>LEFT JOIN</c> ranked with
/// <c>ROW_NUMBER() OVER (PARTITION BY ...)</c>. The parent's filter sits in the
/// join condition, so the database ranks the matching children of every parent.
/// Without the owned types on <see cref="Account"/> the same queries emit
/// <c>NOT EXISTS</c> and these tests pass.
/// </summary>
[Collection("PostgreSQL")]
public sealed class FirstOrDefaultNullGuardRowNumberTests(
    PostgresFixture fixture,
    ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly Id<Workspace> _ownedWorkspaceId = Id<Workspace>.New();
    private static readonly Id<Workspace> _reviewedWorkspaceId = Id<Workspace>.New();
    private static readonly Id<Workspace> _emptyWorkspaceId = Id<Workspace>.New();
    private static readonly Id<Account> _ownerAccountId = Id<Account>.New();

    private readonly List<string> _commands = [];
    private readonly WorkspaceMappers _mappers = new();
    private PostgresTestDatabase _postgres = null!;
    private WorkspaceDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);

        DbContextOptions<WorkspaceDbContext> options = new DbContextOptionsBuilder<WorkspaceDbContext>()
            .UseNpgsql(
                _postgres.ConnectionString,
                npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseArchPillarTypedIds()
            .LogTo(_commands.Add, [RelationalEventId.CommandExecuting])
            .Options;

        _db = new WorkspaceDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        Seed(_db);
        await _db.SaveChangesAsync();
        _commands.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Project_SingleOrDefaultWithFirstOrDefaultNestedMap_DoesNotRankEveryParentAsync()
    {
        WorkspaceDto? result = await _db.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == _ownedWorkspaceId)
            .Project(_mappers.Workspace)
            .SingleOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal("owner@example.com", result.Owner?.Email);
        Assert.Equal((Guid)_ownerAccountId, result.Owner?.Identity.Value);
        Assert.Null(result.Reviewer);
        Assert.Single(result.Notes);
        AssertNoRowNumber();
    }

    [Fact]
    public async Task Project_ToListWithFirstOrDefaultNestedMap_DoesNotRankEveryParentAsync()
    {
        List<WorkspaceDto> result = await _db.Workspaces
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Project(_mappers.Workspace)
            .ToListAsync();

        Assert.Collection(
            result,
            empty =>
            {
                Assert.Null(empty.Owner);
                Assert.Null(empty.Reviewer);
            },
            owned =>
            {
                Assert.Equal("owner@example.com", owned.Owner?.Email);
                Assert.Null(owned.Reviewer);
            },
            reviewed =>
            {
                Assert.Equal("other@example.com", reviewed.Owner?.Email);
                Assert.Equal("owner@example.com", reviewed.Reviewer?.Email);
            });
        AssertNoRowNumber();
    }

    private void AssertNoRowNumber()
    {
        foreach (var command in _commands)
        {
            output.WriteLine(command);
        }

        Assert.DoesNotContain(_commands, c => c.Contains("ROW_NUMBER", StringComparison.Ordinal));
    }

    private static void Seed(WorkspaceDbContext db)
    {
        Account owner = new() { Id = _ownerAccountId, Email = "owner@example.com", FullName = "Owner" };
        Account other = new() { Id = Id<Account>.New(), Email = "other@example.com", FullName = "Other" };
        Employee ownerEmployee = new() { Id = Id<Employee>.New(), Account = owner };
        Employee otherEmployee = new() { Id = Id<Employee>.New(), Account = other };

        db.Workspaces.AddRange(
            new Workspace
            {
                Id = _ownedWorkspaceId,
                Name = "B owned",
                Members = [Member(WorkspaceRole.Owner, ownerEmployee)],
                Notes = [new WorkspaceNote { Id = Id<WorkspaceNote>.New(), Text = "note" }],
            },
            new Workspace
            {
                Id = _reviewedWorkspaceId,
                Name = "C reviewed",
                Members =
                [
                    Member(WorkspaceRole.Owner, otherEmployee),
                    Member(WorkspaceRole.Reviewer, ownerEmployee),
                    Member(WorkspaceRole.Reviewer, otherEmployee, active: false),
                ],
            },
            new Workspace
            {
                Id = _emptyWorkspaceId,
                Name = "A empty",
            });

        static WorkspaceMember Member(WorkspaceRole role, Employee employee, bool active = true) =>
            new() { Id = Id<WorkspaceMember>.New(), Role = role, Active = active, Employee = employee };
    }
}

// ---------------------------------------------------------------------------
// Source entities: a workspace has role-tagged members, each pointing through a
// required Employee to a required Account that owns two consent types.
// ---------------------------------------------------------------------------

public enum WorkspaceRole
{
    Owner = 1,
    Reviewer = 2,
}

public sealed class Workspace
{
    public required Id<Workspace> Id { get; set; }
    public required string Name { get; set; }
    public List<WorkspaceMember> Members { get; set; } = [];
    public List<WorkspaceNote> Notes { get; set; } = [];
}

public sealed class WorkspaceMember
{
    public required Id<WorkspaceMember> Id { get; set; }
    public Id<Workspace> WorkspaceId { get; set; }
    public Id<Employee> EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public bool Active { get; set; }
    public WorkspaceRole Role { get; set; }
}

public sealed class Employee
{
    public required Id<Employee> Id { get; set; }
    public Id<Account> AccountId { get; set; }
    public Account? Account { get; set; }
}

public sealed class Account
{
    public required Id<Account> Id { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public Consent? TermsConsent { get; set; }      // OwnsOne: the trigger
    public Consent? MarketingConsent { get; set; }  // OwnsOne
}

public sealed class Consent
{
    public bool Given { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class WorkspaceNote
{
    public required Id<WorkspaceNote> Id { get; set; }
    public Id<Workspace> WorkspaceId { get; set; }
    public required string Text { get; set; }
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

public sealed record AccountIdentity
{
    public Guid Kind { get; init; }
    public Guid Value { get; init; }
}

public sealed class AccountReferenceDto
{
    public required AccountIdentity Identity { get; init; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
}

public sealed class WorkspaceNoteDto
{
    public required string Text { get; set; }
}

public sealed class WorkspaceDto
{
    public required string Name { get; set; }
    public AccountReferenceDto? Owner { get; set; }
    public AccountReferenceDto? Reviewer { get; set; }
    public required List<WorkspaceNoteDto> Notes { get; set; }
}

// ---------------------------------------------------------------------------
// Mappers: Owner and Reviewer are the two FirstOrDefault() shapes from #104.
// ---------------------------------------------------------------------------

public sealed class WorkspaceMappers : MapperContext
{
    private static readonly Guid _accountKind = new("5c1d7a1e-3f0b-4c6e-9a51-2b8f0d4e7c93");

    public WorkspaceMappers()
    {
        AccountReference = CreateMapper<Account, AccountReferenceDto>(src => new AccountReferenceDto
        {
            Identity = new AccountIdentity { Kind = _accountKind, Value = src.Id },
            Email = src.Email,
            FullName = src.FullName,
            PhoneNumber = src.PhoneNumber,
        });

        WorkspaceNote = CreateMapper<WorkspaceNote, WorkspaceNoteDto>(src => new WorkspaceNoteDto
        {
            Text = src.Text,
        });

        Workspace = CreateMapper<Workspace, WorkspaceDto>(src => new WorkspaceDto
        {
            Name = src.Name,
            Owner = AccountReference.Map(
                src.Members
                    .Where(x => x.Active && x.Role == WorkspaceRole.Owner)
                    .Select(x => x.Employee!.Account!)
                    .FirstOrDefault()!),
            Reviewer = AccountReference.Map(
                src.Members.FirstOrDefault(x => x.Active && x.Role == WorkspaceRole.Reviewer)!.Employee!.Account!),
            Notes = src.Notes.Project(WorkspaceNote).ToList(),
        });
    }

    public Mapper<Account, AccountReferenceDto> AccountReference { get; }
    public Mapper<WorkspaceNote, WorkspaceNoteDto> WorkspaceNote { get; }
    public Mapper<Workspace, WorkspaceDto> Workspace { get; }
}

// ---------------------------------------------------------------------------
// DbContext
// ---------------------------------------------------------------------------

internal sealed class WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(e =>
        {
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.WorkspaceId);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(x => x.WorkspaceId);
        });

        modelBuilder.Entity<WorkspaceMember>(e =>
        {
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).IsRequired();
            e.HasIndex(x => new { x.WorkspaceId, x.Role });
        });

        modelBuilder.Entity<Employee>(e =>
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).IsRequired());

        modelBuilder.Entity<Account>(e =>
        {
            e.OwnsOne(x => x.TermsConsent);
            e.OwnsOne(x => x.MarketingConsent);
        });
    }
}
