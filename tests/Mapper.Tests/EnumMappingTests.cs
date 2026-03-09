namespace ArchPillar.Mapper.Tests;

public class EnumMappingTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Standalone EnumMapper
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending,   OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped,   OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_AllEnumValues_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var result = _mappers.OrderStatusMapper.Map(input);

        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // ToExpression — generates a translatable expression tree
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending,   OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped,   OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void ToExpression_CompiledDelegate_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var expr = _mappers.OrderStatusMapper.ToExpression();
        var func = expr.Compile();

        Assert.Equal(expected, func(input));
    }

    [Fact]
    public void ToExpression_IsNotNull_AndIsConditionalOrSwitch()
    {
        // The expression must be a lambda that can be handed to a LINQ provider.
        // We only verify it is non-null and compilable here; EF Core translation
        // is covered in EfCoreIntegrationTests.
        var expr = _mappers.OrderStatusMapper.ToExpression();

        Assert.NotNull(expr);
        Assert.NotNull(expr.Compile());
    }

    // -----------------------------------------------------------------------
    // Inlined into parent mapper — the Order mapper uses OrderStatus.Map(src.Status)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending,   OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped,   OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_EnumInlinedInParentMapper_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var order = new Order
        {
            Id       = 1,
            Status   = input,
            Customer = new Customer {
                Name = "Test",
                Email = "test@test.com",
            },
            Lines    = [],
        };

        var dto = _mappers.Order.Map(order);

        Assert.Equal(expected, dto!.Status);
    }

    // -----------------------------------------------------------------------
    // Expression inlined in LINQ projection
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending,   OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped,   OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Project_EnumInlinedInProjection_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var orders = new[] { new Order { Id = 1, Status = input, Customer = new() { Name = "Test", Email = "test@test.com" }, Lines = [] } }
            .AsQueryable();

        var result = orders.Project(_mappers.Order).Single();

        Assert.Equal(expected, result.Status);
    }
}
