namespace ArchPillar.Extensions.Mapper.Tests;

public class MapToTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Scalar properties
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_ScalarProperties_AssignsAllRequiredProperties()
    {
        var line = new OrderLine { Id = 1, ProductName = "Widget", Quantity = 5, UnitPrice = 2.50m };
        var dest = new OrderLineDto { ProductName = "", Quantity = 0, UnitPrice = 0m };

        _mappers.OrderLine.MapTo(line, dest);

        Assert.Equal("Widget", dest.ProductName);
        Assert.Equal(5, dest.Quantity);
        Assert.Equal(2.50m, dest.UnitPrice);
    }

    [Fact]
    public void MapTo_ScalarProperties_OverwritesExistingValues()
    {
        var line = new OrderLine { Id = 1, ProductName = "Widget", Quantity = 5, UnitPrice = 2.50m };
        var dest = new OrderLineDto { ProductName = "OLD", Quantity = 99, UnitPrice = 999m };

        _mappers.OrderLine.MapTo(line, dest);

        Assert.Equal("Widget", dest.ProductName);
        Assert.Equal(5, dest.Quantity);
        Assert.Equal(2.50m, dest.UnitPrice);
    }

    // -----------------------------------------------------------------------
    // Optional properties
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_IncludesOptionalProperties()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [],
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = false,
        };

        _mappers.Order.MapTo(order, dest);

        Assert.Equal("Alice", dest.CustomerName);
    }

    // -----------------------------------------------------------------------
    // Collections
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_WithCollection_ReplacesExistingCollection()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Bob", Email = "" },
            Lines    =
            [
                new OrderLine { Id = 1, ProductName = "Widget", Quantity = 2, UnitPrice = 5m },
            ],
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = false,
            Lines    = [new OrderLineDto { ProductName = "OLD", Quantity = 0, UnitPrice = 0m }],
        };

        _mappers.Order.MapTo(order, dest);

        Assert.Single(dest.Lines);
        Assert.Equal("Widget", dest.Lines[0].ProductName);
        Assert.Equal(2, dest.Lines[0].Quantity);
    }

    // -----------------------------------------------------------------------
    // Nested mappers
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_WithNestedMapper_ReplacesNestedObject()
    {
        var user = new User
        {
            Id        = 1,
            FirstName = "Alice",
            LastName  = "Smith",
            Email     = "alice@example.com",
            Role      = UserRole.Admin,
            Address   = new Address { Street = "1 Main St", City = "NYC", Country = "US" },
        };
        var oldAddress = new AddressDto { Street = "OLD", City = "OLD", Country = "OLD" };
        var dest = new UserDto
        {
            Id       = 0,
            FullName = "",
            Email    = "",
            Role     = UserRoleDto.Guest,
            Address  = oldAddress,
        };

        _mappers.User.MapTo(user, dest);

        Assert.Equal("Alice Smith", dest.FullName);
        Assert.NotSame(oldAddress, dest.Address);
        Assert.Equal("1 Main St", dest.Address.Street);
        Assert.Equal("NYC", dest.Address.City);
    }

    // -----------------------------------------------------------------------
    // Variable binding
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_WithVariable_AppliesVariableBinding()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            OwnerId  = 42,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [],
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = false,
        };

        _mappers.Order.MapTo(order, dest, o => o.Set(_mappers.CurrentUserId, 42));

        Assert.True(dest.IsOwner);
    }

    [Fact]
    public void MapTo_WithVariable_DefaultsToZeroWhenNotSet()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            OwnerId  = 42,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [],
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = true,
        };

        _mappers.Order.MapTo(order, dest);

        Assert.False(dest.IsOwner);
    }

    // -----------------------------------------------------------------------
    // Null handling
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_NullSource_LeavesDestinationUnchanged()
    {
        var dest = new OrderLineDto { ProductName = "ORIGINAL", Quantity = 7, UnitPrice = 3.14m };

        _mappers.OrderLine.MapTo(null, dest);

        Assert.Equal("ORIGINAL", dest.ProductName);
        Assert.Equal(7, dest.Quantity);
        Assert.Equal(3.14m, dest.UnitPrice);
    }

    [Fact]
    public void MapTo_NullDestination_ThrowsArgumentNullException()
    {
        var line = new OrderLine { Id = 1, ProductName = "Widget", Quantity = 1, UnitPrice = 1m };

        Assert.Throws<ArgumentNullException>(() => _mappers.OrderLine.MapTo(line, null!));
    }
}
