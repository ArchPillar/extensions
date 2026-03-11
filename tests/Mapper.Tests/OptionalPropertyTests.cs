namespace ArchPillar.Mapper.Tests;

public class OptionalPropertyTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // In-memory mapping — optional properties are always included (null-safe)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_OptionalProperty_AlwaysPopulated()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m, SupplierName = "Sup" }],
        };

        var dto = _mappers.Order.Map(order);

        Assert.Equal("Alice", dto!.CustomerName);
        Assert.Equal("Sup", dto.Lines[0].SupplierName);
    }

    [Fact]
    public void Map_OptionalPropertyWithNullSource_DefaultsToNull()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = null!, Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m }],
        };

        var dto = _mappers.Order.Map(order);

        Assert.Null(dto!.CustomerName);
        Assert.Null(dto.Lines[0].SupplierName);
    }

    [Fact]
    public void Map_NestedOptionalProperty_AlwaysPopulated()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Alice", Email = "" },
            Lines    = [new OrderLine { Id = 1, ProductName = "A", Quantity = 1, UnitPrice = 1m, SupplierName = "SupCo" }],
        };

        var dto = _mappers.Order.Map(order);

        Assert.Equal("SupCo", dto!.Lines[0].SupplierName);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — includes still required (controls what gets queried)
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_WithoutInclude_OptionalPropertyIsNull()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "Dave", Email = "" }, Lines = [] },
        }.AsQueryable();

        var results = orders.Project(_mappers.Order).ToList();

        Assert.Null(results[0].CustomerName);
    }

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

    [Fact]
    public void Project_UnknownStringPath_ThrowsInvalidOperationException()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "", Email = "" }, Lines = [] },
        }.AsQueryable();

        Assert.Throws<InvalidOperationException>(() =>
            orders.Project(_mappers.Order, o => o.Include("NonExistentProperty")).ToList());
    }
}
