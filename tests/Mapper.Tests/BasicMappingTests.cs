namespace ArchPillar.Mapper.Tests;

public class BasicMappingTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Member-init expression style
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_SingleObject_MapsScalarProperties()
    {
        var order = new Order
        {
            Id        = 42,
            CreatedAt = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Status    = OrderStatus.Shipped,
            OwnerId   = 7,
            Customer  = new Customer { Name = "Alice", Email = "alice@example.com" },
            Lines     = [],
        };

        OrderDto dto = _mappers.Order.Map(order);

        Assert.Equal(42, dto!.Id);
        Assert.Equal(new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), dto.PlacedAt);
        Assert.Equal(OrderStatusDto.Shipped, dto.Status);
    }

    [Fact]
    public void Map_SingleObject_MapsNestedCollection()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Bob", Email = "" },
            Lines    =
            [
                new OrderLine { Id = 1, ProductName = "Widget", Quantity = 3, UnitPrice = 9.99m  },
                new OrderLine { Id = 2, ProductName = "Gadget", Quantity = 1, UnitPrice = 49.99m },
            ],
        };

        OrderDto dto = _mappers.Order.Map(order);

        Assert.Equal(2, dto!.Lines.Count);
        Assert.Equal("Widget", dto.Lines[0].ProductName);
        Assert.Equal(3, dto.Lines[0].Quantity);
        Assert.Equal(9.99m, dto.Lines[0].UnitPrice);
        Assert.Equal("Gadget", dto.Lines[1].ProductName);
    }

    // -----------------------------------------------------------------------
    // Property-by-property style (via OrderLine which uses a mix)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_OrderLine_MapsAllRequiredProperties()
    {
        var line = new OrderLine { Id = 1, ProductName = "Widget", Quantity = 5, UnitPrice = 2.50m };

        OrderLineDto dto = _mappers.OrderLine.Map(line);

        Assert.Equal("Widget", dto!.ProductName);
        Assert.Equal(5, dto.Quantity);
        Assert.Equal(2.50m, dto.UnitPrice);
    }

    // -----------------------------------------------------------------------
    // IEnumerable.Project extension — standalone collection mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_Collection_MapsAllElements()
    {
        var lines = new List<OrderLine>
        {
            new() { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m },
            new() { Id = 2, ProductName = "B", Quantity = 2, UnitPrice = 2m },
            new() { Id = 3, ProductName = "C", Quantity = 3, UnitPrice = 3m },
        };

        var dtos = lines.Project(_mappers.OrderLine).ToList();

        Assert.Equal(3, dtos.Count);
        Assert.Equal("A", dtos[0]!.ProductName);
        Assert.Equal("B", dtos[1]!.ProductName);
        Assert.Equal("C", dtos[2]!.ProductName);
    }

    // -----------------------------------------------------------------------
    // Null passthrough
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NullSource_ReturnsNull()
    {
        OrderDto? result = _mappers.Order.Map((Order?)null);

        Assert.Null(result);
    }
}
