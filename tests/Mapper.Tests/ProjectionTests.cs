using System.Linq.Expressions;

namespace ArchPillar.Mapper.Tests;

public class ProjectionTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // ToExpression — shape of the generated expression
    // -----------------------------------------------------------------------

    [Fact]
    public void ToExpression_ReturnsNonNullLambda()
    {
        var expr = _mappers.Order.ToExpression();

        Assert.NotNull(expr);
    }

    [Fact]
    public void ToExpression_ReturnedLambda_IsCompilableAndCorrect()
    {
        var order = new Order
        {
            Id        = 7,
            CreatedAt = new DateTime(2025, 6, 1),
            Status    = OrderStatus.Pending,
            Customer  = new Customer { Name = "Frank", Email = "" },
            Lines     = [],
        };

        var dto = _mappers.Order.ToExpression().Compile()(order);

        Assert.Equal(7, dto.Id);
        Assert.Equal(new DateTime(2025, 6, 1), dto.PlacedAt);
    }

    [Fact]
    public void ToExpression_WithOptions_IncludesOptionalProperties()
    {
        var order = new Order
        {
            Id       = 1,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Grace", Email = "" },
            Lines    = [],
        };

        var dto = _mappers.Order
            .ToExpression(o => o.Include(m => m.CustomerName))
            .Compile()(order);

        Assert.Equal("Grace", dto.CustomerName);
    }

    // -----------------------------------------------------------------------
    // IQueryable.Project extension
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_ReturnsProjectedQueryable()
    {
        var orders = new[]
        {
            new Order { Id = 1, CreatedAt = new DateTime(2025, 1, 1), Status = OrderStatus.Pending, Customer = new Customer { Name = "", Email = "" }, Lines = [] },
            new Order { Id = 2, CreatedAt = new DateTime(2025, 3, 1), Status = OrderStatus.Shipped, Customer = new Customer { Name = "", Email = "" }, Lines = [] },
        }.AsQueryable();

        var results = orders.Project(_mappers.Order).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal(OrderStatusDto.Pending, results[0].Status);
        Assert.Equal(2, results[1].Id);
        Assert.Equal(OrderStatusDto.Shipped, results[1].Status);
    }

    [Fact]
    public void Project_PreservesQueryableComposition()
    {
        // Project must return IQueryable<TDest>, not IEnumerable — further
        // Where/OrderBy/Take must compose without materialising early.
        var orders = Enumerable.Range(1, 10)
            .Select(i => new Order { Id = i, Status = OrderStatus.Pending, Customer = new Customer { Name = "", Email = "" }, Lines = [] })
            .AsQueryable();

        var results = orders
            .Project(_mappers.Order)
            .Where(d => d.Id > 5)
            .ToList();

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Id > 5));
    }

    // -----------------------------------------------------------------------
    // Same options API works for both Project and ToExpression
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_WithIncludeAndSet_AppliesBoth()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 42, Customer = new Customer { Name = "Hank", Email = "" }, Lines = [] },
        }.AsQueryable();

        var results = orders
            .Project(_mappers.Order, o => o
                .Include(m => m.CustomerName)
                .Set(_mappers.CurrentUserId, 42))
            .ToList();

        Assert.Equal("Hank", results[0].CustomerName);
        Assert.True(results[0].IsOwner);
    }

    // -----------------------------------------------------------------------
    // IEnumerable.Project extension
    // -----------------------------------------------------------------------

    [Fact]
    public void ProjectEnumerable_MapsAllElements()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, Customer = new Customer { Name = "A", Email = "" }, Lines = [] },
            new Order { Id = 2, Status = OrderStatus.Shipped, Customer = new Customer { Name = "B", Email = "" }, Lines = [] },
        };

        var results = orders.AsEnumerable().Project(_mappers.Order).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal(2, results[1].Id);
    }

    [Fact]
    public void ProjectEnumerable_WithOptions_AppliesVariableBindings()
    {
        var orders = new[]
        {
            new Order { Id = 1, Status = OrderStatus.Pending, OwnerId = 99, Customer = new Customer { Name = "X", Email = "" }, Lines = [] },
        };

        var results = orders.AsEnumerable()
            .Project(_mappers.Order, o => o.Set(_mappers.CurrentUserId, 99))
            .ToList();

        Assert.True(results[0].IsOwner);
    }

    // -----------------------------------------------------------------------
    // Include cascading into nested mappers
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_WithNestedInclude_CascadesToChildMapper()
    {
        var orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "", Email = "" },
                Lines    =
                [
                    new OrderLine
                    {
                        Id           = 10,
                        ProductName  = "Widget",
                        Quantity     = 2,
                        UnitPrice    = 9.99m,
                        SupplierName = "Acme",
                    },
                ],
            },
        }.AsQueryable();

        // Include the optional SupplierName on the nested OrderLineDto
        var results = orders
            .Project(_mappers.Order, o => o
                .Include(m => m.Lines, line => line
                    .Include(l => l.SupplierName)))
            .ToList();

        Assert.Equal("Acme", results[0].Lines[0].SupplierName);
    }

    [Fact]
    public void Project_WithStringPathInclude_CascadesToChildMapper()
    {
        var orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "", Email = "" },
                Lines    =
                [
                    new OrderLine
                    {
                        Id           = 10,
                        ProductName  = "Widget",
                        Quantity     = 2,
                        UnitPrice    = 9.99m,
                        SupplierName = "Supplier1",
                    },
                ],
            },
        }.AsQueryable();

        var results = orders
            .Project(_mappers.Order, o => o.Include("Lines.SupplierName"))
            .ToList();

        Assert.Equal("Supplier1", results[0].Lines[0].SupplierName);
    }

    [Fact]
    public void Project_WithoutInclude_OptionalPropertyIsDefault()
    {
        var orders = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = OrderStatus.Pending,
                Customer = new Customer { Name = "Jane", Email = "" },
                Lines    = [],
            },
        }.AsQueryable();

        // No includes — optional CustomerName should be null
        var results = orders.Project(_mappers.Order).ToList();

        Assert.Null(results[0].CustomerName);
    }
}
