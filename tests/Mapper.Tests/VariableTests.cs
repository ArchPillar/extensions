namespace ArchPillar.Mapper.Tests;

public class VariableTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // In-memory mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_WithVariableSet_SubstitutesValue()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 99, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        var dto = _mappers.Order.Map(order, o => o.Set(_mappers.CurrentUserId, 99));

        Assert.True(dto!.IsOwner);
    }

    [Fact]
    public void Map_WithVariableSetToNonMatchingValue_IsOwnerFalse()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 99, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        var dto = _mappers.Order.Map(order, o => o.Set(_mappers.CurrentUserId, 1));

        Assert.False(dto!.IsOwner);
    }

    [Fact]
    public void Map_WithVariableNotSet_DefaultsToDefaultT()
    {
        // OwnerId = 99, variable not set => defaults to 0 => IsOwner = false
        var order = new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 99, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        var dto = _mappers.Order.Map(order);

        Assert.False(dto!.IsOwner);
    }

    [Fact]
    public void Map_OwnerIdZero_WithVariableNotSet_IsOwnerTrue()
    {
        // OwnerId = 0, variable not set => default(int) = 0 => IsOwner = true
        var order = new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 0, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        var dto = _mappers.Order.Map(order);

        Assert.True(dto!.IsOwner);
    }

    // -----------------------------------------------------------------------
    // LINQ projection
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_WithVariableSet_SubstitutesValueInExpression()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 5,  Customer = new Customer { Name = "", Email = "" }, Lines = [] },
            new Order { Id = 2, Status = OrderStatus.Pending, OwnerId = 9,  Customer = new Customer { Name = "", Email = "" }, Lines = [] },
        }.AsQueryable();

        var results = orders
            .Project(_mappers.Order, o => o.Set(_mappers.CurrentUserId, 5))
            .ToList();

        Assert.True(results[0].IsOwner);
        Assert.False(results[1].IsOwner);
    }

    [Fact]
    public void Project_WithVariableNotSet_UsesDefaultT()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 0,  Customer = new Customer { Name = "", Email = "" }, Lines = [] },
            new Order { Id = 2, Status = OrderStatus.Pending, OwnerId = 5,  Customer = new Customer { Name = "", Email = "" }, Lines = [] },
        }.AsQueryable();

        var results = orders.Project(_mappers.Order).ToList();

        Assert.True(results[0].IsOwner);   // OwnerId 0 == default(int) 0
        Assert.False(results[1].IsOwner);
    }

    // -----------------------------------------------------------------------
    // Variable propagation into nested mappers
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_VariablePropagatedToNestedMapper()
    {
        // User.Orders is optional — always included for in-memory mapping.
        // The variable binding must propagate from the User Map call into the nested Order mapper.
        var user = new User
        {
            Id = 1, FirstName = "Alice", LastName = "Smith", Email = "a@b.com",
            Role = UserRole.Member, Address = new Address { Street = "1st", City = "NY", Country = "US" },
            Orders =
            [
                new Order { Id = 10, Status = OrderStatus.Pending, OwnerId = 42, Customer = new Customer { Name = "", Email = "" }, Lines = [] },
            ],
        };

        var dto = _mappers.User.Map(user, o => o
            .Set(_mappers.CurrentUserId, 42));

        Assert.True(dto!.Orders![0].IsOwner);
    }

    [Fact]
    public void Project_VariablePropagatedToNestedMapper_ViaInclude()
    {
        var users = new[]
        {
            new User
            {
                Id = 1, FirstName = "Bob", LastName = "Jones", Email = "b@b.com",
                Role = UserRole.Admin, Address = new Address { Street = "2nd", City = "LA", Country = "US" },
                Orders =
                [
                    new Order { Id = 20, Status = OrderStatus.Shipped, OwnerId = 7, Customer = new Customer { Name = "", Email = "" }, Lines = [] },
                ],
            },
        }.AsQueryable();

        var results = users
            .Project(_mappers.User, o => o
                .Include(u => u.Orders)
                .Set(_mappers.CurrentUserId, 7))
            .ToList();

        Assert.True(results[0].Orders![0].IsOwner);
    }

    // -----------------------------------------------------------------------
    // Variable identity — only the exact instance used at definition time works
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_SettingUnrelatedVariable_HasNoEffect()
    {
        // A second context has its own CurrentUserId variable instance.
        // Setting that other instance should not affect _mappers.Order.
        var other = new TestMappers();
        var order = new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 5, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        // Set the *other* context's variable, not _mappers.CurrentUserId
        var dto = _mappers.Order.Map(order, o => o.Set(other.CurrentUserId, 5));

        // The unrelated variable has no effect; variable resolves to default (0 != 5)
        Assert.False(dto!.IsOwner);
    }
}
