namespace ArchPillar.Extensions.Mapper;

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
    // Collections — Shallow (default)
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_Shallow_ReplacesCollectionReference()
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
        var originalLines = new List<OrderLineDto>
        {
            new() { ProductName = "OLD", Quantity = 0, UnitPrice = 0m },
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = false,
            Lines    = originalLines,
        };

        _mappers.Order.MapTo(order, dest);

        Assert.NotSame(originalLines, dest.Lines);
        Assert.Single(dest.Lines);
        Assert.Equal("Widget", dest.Lines[0].ProductName);
        Assert.Equal(2, dest.Lines[0].Quantity);
    }

    // -----------------------------------------------------------------------
    // Collections — Deep
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_Deep_PreservesCollectionInstance()
    {
        var mappers = new DeepCollectionMappers();
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
        var originalLines = new List<OrderLineDto>
        {
            new() { ProductName = "OLD", Quantity = 0, UnitPrice = 0m },
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = false,
            Lines    = originalLines,
        };

        mappers.Order.MapTo(order, dest);

        Assert.Same(originalLines, dest.Lines);
        Assert.Single(dest.Lines);
        Assert.Equal("Widget", dest.Lines[0].ProductName);
    }

    [Fact]
    public void MapTo_Deep_NullDestinationCollection_AssignsNewCollection()
    {
        var mappers = new DeepCollectionMappers();
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
            Lines    = null!,
        };

        mappers.Order.MapTo(order, dest);

        Assert.NotNull(dest.Lines);
        Assert.Single(dest.Lines);
        Assert.Equal("Widget", dest.Lines[0].ProductName);
    }

    // -----------------------------------------------------------------------
    // Collections — Deep: null scenarios
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_Deep_NullSourceCollection_ClearsDestination()
    {
        var mappers = new DeepCollectionMappers();
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Bob", Email = "" },
            Lines    = null!,
        };
        var originalLines = new List<OrderLineDto>
        {
            new() { ProductName = "Existing", Quantity = 1, UnitPrice = 1m },
        };
        var dest = new OrderDto
        {
            Id       = 0,
            PlacedAt = DateTime.MinValue,
            Status   = OrderStatusDto.Pending,
            IsOwner  = false,
            Lines    = originalLines,
        };

        mappers.Order.MapTo(order, dest);

        Assert.Same(originalLines, dest.Lines);
        Assert.Empty(dest.Lines);
    }

    // -----------------------------------------------------------------------
    // Collections — DeepWithIdentity: null scenarios
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_DeepWithIdentity_NullDestinationCollection_IsNoOp()
    {
        var mappers = new IdentityCollectionMappers();
        var board = new TaskBoard
        {
            Id    = 1,
            Items = [new TaskItem { Id = 10, Title = "New", Done = false }],
        };
        var dest = new TaskBoardDto { Id = 0, Items = null! };

        mappers.TaskBoard.MapTo(board, dest);

        Assert.Null(dest.Items);
    }

    [Fact]
    public void MapTo_DeepWithIdentity_NullSourceCollection_ClearsDestination()
    {
        var mappers = new IdentityCollectionMappers();
        var board = new TaskBoard { Id = 1, Items = null! };
        var originalList = new List<TaskItemDto>
        {
            new() { Id = 10, Title = "Existing", Done = false },
        };
        var dest = new TaskBoardDto { Id = 0, Items = originalList };

        mappers.TaskBoard.MapTo(board, dest);

        Assert.Same(originalList, dest.Items);
        Assert.Empty(dest.Items);
    }

    // -----------------------------------------------------------------------
    // Collections — DeepWithIdentity: merge scenarios
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_DeepWithIdentity_UpdatesExistingItemsInPlace()
    {
        var mappers = new IdentityCollectionMappers();
        var board = new TaskBoard
        {
            Id    = 1,
            Items = [new TaskItem { Id = 10, Title = "Updated", Done = true }],
        };
        var existingItem = new TaskItemDto { Id = 10, Title = "Original", Done = false };
        var originalList = new List<TaskItemDto> { existingItem };
        var dest = new TaskBoardDto { Id = 0, Items = originalList };

        mappers.TaskBoard.MapTo(board, dest);

        Assert.Same(originalList, dest.Items);
        Assert.Single(dest.Items);
        Assert.Same(existingItem, dest.Items[0]);
        Assert.Equal("Updated", existingItem.Title);
        Assert.True(existingItem.Done);
    }

    [Fact]
    public void MapTo_DeepWithIdentity_AddsNewItems()
    {
        var mappers = new IdentityCollectionMappers();
        var board = new TaskBoard
        {
            Id    = 1,
            Items =
            [
                new TaskItem { Id = 10, Title = "Existing", Done = false },
                new TaskItem { Id = 20, Title = "Brand New", Done = true },
            ],
        };
        var existingItem = new TaskItemDto { Id = 10, Title = "Existing", Done = false };
        var dest = new TaskBoardDto { Id = 0, Items = new List<TaskItemDto> { existingItem } };

        mappers.TaskBoard.MapTo(board, dest);

        Assert.Equal(2, dest.Items.Count);
        Assert.Same(existingItem, dest.Items[0]);
        Assert.Equal("Brand New", dest.Items[1].Title);
    }

    [Fact]
    public void MapTo_DeepWithIdentity_RemovesUnmatchedDestItems()
    {
        var mappers = new IdentityCollectionMappers();
        var board = new TaskBoard { Id = 1, Items = [] };
        var dest = new TaskBoardDto
        {
            Id    = 0,
            Items = [new TaskItemDto { Id = 99, Title = "Orphan", Done = false }],
        };

        mappers.TaskBoard.MapTo(board, dest);

        Assert.Empty(dest.Items);
    }

    [Fact]
    public void MapTo_DeepWithIdentity_FullMerge_UpdatesAddsAndRemoves()
    {
        var mappers = new IdentityCollectionMappers();
        var board = new TaskBoard
        {
            Id    = 1,
            Items =
            [
                new TaskItem { Id = 10, Title = "Updated A", Done = true },
                new TaskItem { Id = 30, Title = "New C", Done = false },
            ],
        };
        var itemA = new TaskItemDto { Id = 10, Title = "A", Done = false };
        var itemB = new TaskItemDto { Id = 20, Title = "B", Done = false };
        var originalList = new List<TaskItemDto> { itemA, itemB };
        var dest = new TaskBoardDto { Id = 0, Items = originalList };

        mappers.TaskBoard.MapTo(board, dest);

        Assert.Same(originalList, dest.Items);
        Assert.Equal(2, dest.Items.Count);

        // itemA was updated in place
        Assert.Same(itemA, dest.Items[0]);
        Assert.Equal("Updated A", itemA.Title);
        Assert.True(itemA.Done);

        // itemB was removed, new C was added
        Assert.DoesNotContain(itemB, dest.Items);
        Assert.Equal("New C", dest.Items[1].Title);
        Assert.Equal(30, dest.Items[1].Id);
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
