namespace ArchPillar.Mapper.Tests;

public class OptionalPropertyTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Omitted optionals default to null
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_WithoutInclude_OptionalPropertyIsNull()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m, SupplierName = "Sup" }],
        };

        var dto = _mappers.Order.Map(order);

        Assert.Null(dto!.CustomerName);
        Assert.Null(dto.Lines[0].SupplierName);
    }

    // -----------------------------------------------------------------------
    // Typed lambda Include
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_IncludeTopLevelOptional_WithLambda_PopulatesProperty()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "Alice", Email = "" }, Lines = [] };

        var dto = _mappers.Order.Map(order, o => o.Include(m => m.CustomerName));

        Assert.Equal("Alice", dto!.CustomerName);
    }

    [Fact]
    public void Map_IncludeNestedOptional_WithLambdaChain_PopulatesNestedProperty()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m, SupplierName = "SupCo" }],
        };

        var dto = _mappers.Order.Map(
            order,
            o => o.Include(m => m.Lines, line => line.Include(l => l.SupplierName)));

        Assert.Null(dto!.CustomerName);                  // not requested
        Assert.Equal("SupCo", dto.Lines[0].SupplierName);
    }

    [Fact]
    public void Map_IncludeTopLevelAndNestedOptional_WithLambda_PopulatesBoth()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m, SupplierName = "SupCo" }],
        };

        var dto = _mappers.Order.Map(order, o => o
            .Include(m => m.CustomerName)
            .Include(m => m.Lines, line => line.Include(l => l.SupplierName)));

        Assert.Equal("Alice",  dto!.CustomerName);
        Assert.Equal("SupCo", dto.Lines[0].SupplierName);
    }

    // -----------------------------------------------------------------------
    // String-path Include
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_IncludeTopLevelOptional_WithStringPath_PopulatesProperty()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "Bob", Email = "" }, Lines = [] };

        var dto = _mappers.Order.Map(order, o => o.Include("CustomerName"));

        Assert.Equal("Bob", dto!.CustomerName);
    }

    [Fact]
    public void Map_IncludeNestedOptional_WithDotNotation_PopulatesNestedProperty()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Bob", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "B", Quantity = 2, UnitPrice = 5m, SupplierName = "MegaCo" }],
        };

        var dto = _mappers.Order.Map(order, o => o.Include("Lines.SupplierName"));

        Assert.Equal("MegaCo", dto!.Lines[0].SupplierName);
    }

    [Fact]
    public void Map_CombineLambdaAndStringIncludes_BothApplied()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Carol", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "C", Quantity = 1, UnitPrice = 1m, SupplierName = "SmallCo" }],
        };

        var dto = _mappers.Order.Map(order, o => o
            .Include(m => m.CustomerName)
            .Include("Lines.SupplierName"));

        Assert.Equal("Carol",   dto!.CustomerName);
        Assert.Equal("SmallCo", dto.Lines[0].SupplierName);
    }

    [Fact]
    public void Map_UnknownStringPath_ThrowsInvalidOperationException()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "", Email = "" }, Lines = [] };

        Assert.Throws<InvalidOperationException>(() =>
            _mappers.Order.Map(order, o => o.Include("NonExistentProperty")));
    }

    // -----------------------------------------------------------------------
    // LINQ projection — same options API
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_IncludeOptional_WithLambda_PopulatesInProjection()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "Dave", Email = "" }, Lines = [] },
        }.AsQueryable();

        var results = orders
            .Project(_mappers.Order, o => o.Include(m => m.CustomerName))
            .ToList();

        Assert.Equal("Dave", results[0].CustomerName);
    }

    [Fact]
    public void Project_IncludeNestedOptional_WithStringPath_PopulatesInProjection()
    {
        var orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "Eve", Email = "" },
                Lines    = [new OrderLine { Id = 1, ProductName = "X", Quantity = 1, UnitPrice = 1m, SupplierName = "VendorX" }],
            },
        }.AsQueryable();

        var results = orders
            .Project(_mappers.Order, o => o.Include("Lines.SupplierName"))
            .ToList();

        Assert.Equal("VendorX", results[0].Lines[0].SupplierName);
    }
}
