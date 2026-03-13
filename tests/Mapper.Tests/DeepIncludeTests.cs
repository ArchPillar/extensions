namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Verifies that optional-property includes cascade correctly through multiple
/// levels of nested mappers.
/// <para>
/// Hierarchy under test: <c>User → Order → OrderLine</c> (3 levels).
/// <list type="bullet">
/// <item><c>User.Orders</c> — optional collection at level 1.</item>
/// <item><c>Order.CustomerName</c> — optional scalar at level 2.</item>
/// <item><c>Order.Lines</c> — required collection (always projected), acts as
/// a transparent pass-through for level-3 includes.</item>
/// <item><c>OrderLine.SupplierName</c> — optional scalar at level 3.</item>
/// </list>
/// </para>
/// <para>
/// Key scenario: including <c>"Orders.Lines.SupplierName"</c> must cascade
/// the <c>SupplierName</c> include all the way down through the required
/// <c>Lines</c> property without also including the sibling optional
/// <c>CustomerName</c>.
/// </para>
/// </summary>
public class DeepIncludeTests
{
    private readonly TestMappers _mappers = new();

    private static User MakeUser() => new()
    {
        Id        = 1,
        FirstName = "Alice",
        LastName  = "Smith",
        Email     = "a@b.com",
        Role      = UserRole.Member,
        Address   = new Address { Street = "1st", City = "NY", Country = "US" },
        Orders    =
        [
            new Order
            {
                Id       = 10,
                Status   = OrderStatus.Pending,
                OwnerId  = 0,
                Customer = new Customer { Name = "Dave", Email = "" },
                Lines    =
                [
                    new OrderLine { Id = 1, ProductName = "Widget", Quantity = 1, UnitPrice = 5m, SupplierName = "AcmeCo" },
                ],
            },
        ],
    };

    // -----------------------------------------------------------------------
    // Level 1 include only — no deeper cascade
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_IncludeOrders_Level2And3OptionalsNotPopulated()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        var results = users
            .Project(_mappers.User, o => o.Include(u => u.Orders))
            .ToList();

        Assert.NotNull(results[0].Orders);
        Assert.Null(results[0].Orders![0].CustomerName);          // level-2 optional not requested
        Assert.Null(results[0].Orders![0].Lines[0].SupplierName); // level-3 optional not requested
    }

    // -----------------------------------------------------------------------
    // Level 2 include — cascade stops at level 2
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_IncludeOrdersAndCustomerName_Level3NotPopulated()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        var results = users
            .Project(_mappers.User, o => o
                .Include(u => u.Orders!, orderOpts => orderOpts
                    .Include(ord => ord.CustomerName)))
            .ToList();

        Assert.NotNull(results[0].Orders);
        Assert.Equal("Dave", results[0].Orders![0].CustomerName);  // level-2 included
        Assert.Null(results[0].Orders![0].Lines[0].SupplierName);  // level-3 still absent
    }

    // -----------------------------------------------------------------------
    // Level 3 string-path include — skips a required level (Lines)
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_StringPath_DeepInclude_SkipsRequiredLevel_SupplierNamePopulated()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        // "Orders.Lines.SupplierName": Orders (optional L1) → Lines (required, transparent)
        // → SupplierName (optional L3). CustomerName (optional L2) is NOT in the path.
        var results = users
            .Project(_mappers.User, o => o.Include("Orders.Lines.SupplierName"))
            .ToList();

        Assert.NotNull(results[0].Orders);
        Assert.Null(results[0].Orders![0].CustomerName);                     // L2 not included
        Assert.Equal("AcmeCo", results[0].Orders![0].Lines[0].SupplierName); // L3 included
    }

    // -----------------------------------------------------------------------
    // Full deep include — both L2 and L3 included simultaneously
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_FullDeepInclude_AllOptionalsAtAllLevelsPopulated()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        var results = users
            .Project(_mappers.User, o => o
                .Include(u => u.Orders!, orderOpts => orderOpts
                    .Include(ord => ord.CustomerName)
                    .Include("Lines.SupplierName")))
            .ToList();

        Assert.NotNull(results[0].Orders);
        Assert.Equal("Dave",   results[0].Orders![0].CustomerName);           // L2 included
        Assert.Equal("AcmeCo", results[0].Orders![0].Lines[0].SupplierName);  // L3 included
    }

    // -----------------------------------------------------------------------
    // Without any include — top-level optional absent, nothing below it either
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_NoIncludes_OrdersIsNull()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        var results = users
            .Project(_mappers.User)
            .ToList();

        Assert.Null(results[0].Orders);
    }

    // -----------------------------------------------------------------------
    // In-memory Map always includes all optionals — verify cascades correctly
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_InMemory_AllOptionalsAtAllLevelsAlwaysPopulated()
    {
        UserDto? dto = _mappers.User.Map(MakeUser());

        Assert.NotNull(dto!.Orders);
        Assert.Equal("Dave",   dto.Orders![0].CustomerName);
        Assert.Equal("AcmeCo", dto.Orders![0].Lines[0].SupplierName);
    }

    // -----------------------------------------------------------------------
    // Deep include validation — typos in nested paths throw
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_UnknownNestedStringPath_ThrowsInvalidOperationException()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        Assert.Throws<InvalidOperationException>(() =>
            users.Project(_mappers.User, o => o.Include("Orders.Typo")).ToList());
    }

    [Fact]
    public void Project_UnknownDeeplyNestedStringPath_ThrowsInvalidOperationException()
    {
        IQueryable<User> users = new[] { MakeUser() }.AsQueryable();

        Assert.Throws<InvalidOperationException>(() =>
            users.Project(_mappers.User, o => o.Include("Orders.Lines.Typo")).ToList());
    }
}
