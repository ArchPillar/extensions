using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that a nested <c>Map(...)</c> over a <c>FirstOrDefault()</c> /
/// <c>SingleOrDefault()</c> source is guarded by an existence check rather than
/// an entity comparison, so EF Core emits <c>EXISTS</c> instead of joining the
/// subquery ranked with <c>ROW_NUMBER()</c> — while keeping the exact
/// <c>FirstOrDefault() == null</c> semantics in memory and in SQL.
/// </summary>
public sealed class FirstOrDefaultNullGuardTests : IDisposable
{
    private readonly GuardDbContext _db;
    private readonly GuardMappers _mappers = new();

    public FirstOrDefaultNullGuardTests()
    {
        DbContextOptionsBuilder<GuardDbContext> builder = new DbContextOptionsBuilder<GuardDbContext>()
            .UseSqlite("DataSource=:memory:");
        builder.UseArchPillarMapper();

        _db = new GuardDbContext(builder.Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _db.Orders.AddRange(CreateOrders());
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public void ToExpression_FirstOrDefaultSource_DoesNotCompareEntityToNull()
    {
        var finder = new EntityNullComparisonFinder();
        finder.Visit(_mappers.Order.ToExpression());

        Assert.False(finder.Found);
    }

    [Fact]
    public void Project_FirstOrDefaultSource_GuardsWithExistsInsteadOfRowNumber()
    {
        var sql = _db.Orders.Project(_mappers.Order).ToQueryString();

        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_FirstOrDefaultSource_MapsPresentAndMissingAsync()
    {
        List<GuardOrderDto> results = await _db.Orders
            .OrderBy(o => o.Id)
            .Project(_mappers.Order)
            .ToListAsync();

        AssertMapped(results);
    }

    [Fact]
    public async Task InlinedMap_FirstOrDefaultSource_GuardsWithExistsAsync()
    {
        IQueryable<GuardPersonDto?> query = _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => _mappers.Person.Map(
                o.Contacts.Where(c => c.IsPrimary).Select(c => c.Person).FirstOrDefault()!));

        var sql = query.ToQueryString();
        List<GuardPersonDto?> results = await query.ToListAsync();

        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS", sql, StringComparison.Ordinal);
        Assert.Equal("Alice", results[0]?.Name);
        Assert.Null(results[1]);
    }

    [Fact]
    public void Map_FirstOrDefaultSource_InMemory_MapsPresentAndMissing()
    {
        var results = CreateOrders().Select(o => _mappers.Order.Map(o)).ToList();

        AssertMapped(results);
    }

    private sealed class EntityNullComparisonFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Equal
                && node.Left is MethodCallExpression { Method.Name: "FirstOrDefault" or "SingleOrDefault" })
            {
                Found = true;
            }

            return base.VisitBinary(node);
        }
    }

    private static void AssertMapped(List<GuardOrderDto> results)
    {
        Assert.Equal(2, results.Count);

        GuardOrderDto withContacts = results[0];
        Assert.Equal("Alice", withContacts.Primary?.Name);
        Assert.Equal("Bob", withContacts.Named?.Name);
        Assert.Equal("Alice", withContacts.Only?.Name);

        // The first contact has no backup: FirstOrDefault() is null even though
        // a later contact has one, so the guard must yield null rather than map.
        Assert.Null(withContacts.FirstBackup);

        GuardOrderDto withoutContacts = results[1];
        Assert.Null(withoutContacts.Primary);
        Assert.Null(withoutContacts.Named);
        Assert.Null(withoutContacts.Only);
        Assert.Null(withoutContacts.FirstBackup);
    }

    private static List<GuardOrder> CreateOrders()
    {
        var alice = new GuardPerson { Id = 1, Name = "Alice" };
        var bob   = new GuardPerson { Id = 2, Name = "Bob" };
        var carol = new GuardPerson { Id = 3, Name = "Carol" };

        return
        [
            new GuardOrder
            {
                Id = 1,
                Contacts =
                [
                    new GuardContact { Id = 1, IsPrimary = true, PersonId = 1, Person = alice },
                    new GuardContact { Id = 2, IsPrimary = false, PersonId = 2, Person = bob, BackupId = 3, Backup = carol },
                ],
            },
            new GuardOrder { Id = 2, Contacts = [] },
        ];
    }
}

// ---------------------------------------------------------------------------
// Test-local models (internal for EF Core reflection)
// ---------------------------------------------------------------------------

internal class GuardOrder
{
    public int Id { get; set; }
    public List<GuardContact> Contacts { get; set; } = [];
}

internal class GuardContact
{
    public int Id { get; set; }
    public bool IsPrimary { get; set; }
    public int PersonId { get; set; }
    public required GuardPerson Person { get; set; }
    public int? BackupId { get; set; }
    public GuardPerson? Backup { get; set; }
}

internal class GuardPerson
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

internal sealed class GuardOrderDto
{
    public required int Id { get; set; }
    public GuardPersonDto? Primary { get; set; }
    public GuardPersonDto? Named { get; set; }
    public GuardPersonDto? Only { get; set; }
    public GuardPersonDto? FirstBackup { get; set; }
}

internal sealed class GuardPersonDto
{
    public required string Name { get; set; }
}

internal sealed class GuardMappers : MapperContext
{
    public Mapper<GuardPerson, GuardPersonDto> Person { get; }
    public Mapper<GuardOrder, GuardOrderDto> Order { get; }

    public GuardMappers()
    {
        Person = CreateMapper<GuardPerson, GuardPersonDto>(p => new GuardPersonDto
        {
            Name = p.Name,
        });

        Order = CreateMapper<GuardOrder, GuardOrderDto>(o => new GuardOrderDto
        {
            Id = o.Id,
            Primary = Person.Map(o.Contacts.Where(c => c.IsPrimary).Select(c => c.Person).FirstOrDefault()!),
            Named = Person.Map(o.Contacts.Select(c => c.Person).FirstOrDefault(p => p.Name == "Bob")!),
            Only = Person.Map(o.Contacts.Where(c => c.IsPrimary).Select(c => c.Person).SingleOrDefault()!),
            FirstBackup = Person.Map(o.Contacts.OrderBy(c => c.Id).Select(c => c.Backup).FirstOrDefault()!),
        });
    }
}

internal sealed class GuardDbContext(DbContextOptions<GuardDbContext> options) : DbContext(options)
{
    public DbSet<GuardOrder> Orders => Set<GuardOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuardOrder>().HasMany(o => o.Contacts).WithOne().HasForeignKey("OrderId");
        modelBuilder.Entity<GuardContact>().HasOne(c => c.Person).WithMany().HasForeignKey(c => c.PersonId);
        modelBuilder.Entity<GuardContact>().HasOne(c => c.Backup).WithMany().HasForeignKey(c => c.BackupId);
    }
}
