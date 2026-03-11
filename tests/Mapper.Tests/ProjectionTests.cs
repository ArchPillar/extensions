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
}
