namespace ArchPillar.Extensions.Mapper;

public class NestedMapperTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // In-memory — nested collection mapper reuse
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_WithNestedCollection_UsesNestedMapper()
    {
        var order = new Order
        {
            Id       = 10,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    =
            [
                new OrderLine { Id = 1, ProductName = "Alpha", Quantity = 2, UnitPrice = 5.00m  },
                new OrderLine { Id = 2, ProductName = "Beta",  Quantity = 1, UnitPrice = 15.00m },
            ],
        };

        OrderDto? dto = _mappers.Order.Map(order);

        Assert.Equal(2, dto!.Lines.Count);
        Assert.Equal("Alpha", dto.Lines[0].ProductName);
        Assert.Equal(2, dto.Lines[0].Quantity);
        Assert.Equal(5.00m, dto.Lines[0].UnitPrice);
        Assert.Equal("Beta", dto.Lines[1].ProductName);
    }

    [Fact]
    public void Map_EmptyCollection_ReturnsEmptyCollection()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        OrderDto? dto = _mappers.Order.Map(order);

        Assert.NotNull(dto!.Lines);
        Assert.Empty(dto.Lines);
    }

    // -----------------------------------------------------------------------
    // Nested mapper optional properties are always included for in-memory mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NestedOptionalProperty_AlwaysPopulated()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Bob", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m, SupplierName = "SupX" }],
        };

        OrderDto? dto = _mappers.Order.Map(order);

        Assert.Equal("Bob", dto!.CustomerName);
        Assert.Equal("SupX", dto.Lines[0].SupplierName);
    }

    // -----------------------------------------------------------------------
    // Standalone nested mapper vs. mapper inlined in parent — same result
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NestedMapperStandalone_ProducesSameResultAsInlinedMapper()
    {
        var line = new OrderLine { Id = 1, ProductName = "Widget", Quantity = 4, UnitPrice = 3.50m };

        OrderLineDto fromParent = _mappers.Order.Map(new Order
        {
            Id = 1,
            Status = OrderStatus.Pending,
            Customer = new Customer { Name = "", Email = "" },
            Lines = [line],
        })!.Lines[0];

        OrderLineDto? standalone = _mappers.OrderLine.Map(line);

        Assert.Equal(standalone!.ProductName, fromParent.ProductName);
        Assert.Equal(standalone.Quantity, fromParent.Quantity);
        Assert.Equal(standalone.UnitPrice, fromParent.UnitPrice);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — nested mapper is inlined (not called as delegate)
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_WithNestedCollection_InlinesNestedExpression()
    {
        IQueryable<Order> orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "Carol", Email = "" },
                Lines    =
                [
                    new OrderLine { Id = 1, ProductName = "P1", Quantity = 3, UnitPrice = 7m  },
                    new OrderLine { Id = 2, ProductName = "P2", Quantity = 1, UnitPrice = 20m },
                ],
            },
        }.AsQueryable();

        var results = orders.Project(_mappers.Order).ToList();

        Assert.Single(results);
        Assert.Equal(2, results[0].Lines.Count);
        Assert.Equal("P1", results[0].Lines[0].ProductName);
        Assert.Equal("P2", results[0].Lines[1].ProductName);
    }

    // -----------------------------------------------------------------------
    // Declaration order does not matter — nested mappers are resolved lazily
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_WithReverseDeclaredMappers_WorksCorrectly()
    {
        // ReverseOrderMappers declares Order BEFORE OrderLine — the opposite
        // of dependency order.  This verifies that nested mapper inlining is
        // deferred until first use rather than evaluated at build time.
        var mappers = new ReverseOrderMappers();

        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    =
            [
                new OrderLine { Id = 1, ProductName = "Alpha", Quantity = 2, UnitPrice = 5.00m },
                new OrderLine { Id = 2, ProductName = "Beta",  Quantity = 1, UnitPrice = 15.00m },
            ],
        };

        OrderDto dto = mappers.Order.Map(order);

        Assert.Equal(2, dto!.Lines.Count);
        Assert.Equal("Alpha", dto.Lines[0].ProductName);
        Assert.Equal("Beta", dto.Lines[1].ProductName);
    }

    [Fact]
    public void Project_WithReverseDeclaredMappers_WorksCorrectly()
    {
        var mappers = new ReverseOrderMappers();

        IQueryable<Order> orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "Carol", Email = "" },
                Lines    = [new OrderLine { Id = 1, ProductName = "P1", Quantity = 3, UnitPrice = 7m }],
            },
        }.AsQueryable();

        var results = orders.Project(mappers.Order).ToList();

        Assert.Single(results);
        Assert.Equal("P1", results[0].Lines[0].ProductName);
    }

    [Fact]
    public void Project_NestedOptionalIncluded_AppearsinProjection()
    {
        IQueryable<Order> orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "Dave", Email = "" },
                Lines    = [new OrderLine { Id = 1, ProductName = "X", Quantity = 1, UnitPrice = 1m, SupplierName = "S1" }],
            },
        }.AsQueryable();

        var results = orders
            .Project(_mappers.Order, o => o.Include(m => m.Lines, l => l.Include(l2 => l2.SupplierName)))
            .ToList();

        Assert.Equal("S1", results[0].Lines[0].SupplierName);
    }
}
