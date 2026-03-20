namespace ArchPillar.Extensions.Mapper.Tests;

public class EnumMappingTests
{
    private readonly TestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Standalone EnumMapper
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_AllEnumValues_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        OrderStatusDto result = _mappers.OrderStatusMapper.Map(input);

        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // ToExpression — generates a translatable expression tree
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void ToExpression_CompiledDelegate_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var expr = _mappers.OrderStatusMapper.ToExpression();
        Func<OrderStatus, OrderStatusDto> func = expr.Compile();

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
    // Large enum (11 values) — the scenario that triggered the original bug
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PropertyType.Invalid, PropertyTypeDto.Other)]
    [InlineData(PropertyType.Other, PropertyTypeDto.Other)]
    [InlineData(PropertyType.House, PropertyTypeDto.House)]
    [InlineData(PropertyType.RowHouse, PropertyTypeDto.RowHouse)]
    [InlineData(PropertyType.Apartment, PropertyTypeDto.Apartment)]
    [InlineData(PropertyType.Recreational, PropertyTypeDto.Recreational)]
    [InlineData(PropertyType.Cooperative, PropertyTypeDto.Cooperative)]
    [InlineData(PropertyType.Farm, PropertyTypeDto.Farm)]
    [InlineData(PropertyType.LandLeisure, PropertyTypeDto.LandLeisure)]
    [InlineData(PropertyType.LandResidence, PropertyTypeDto.LandResidence)]
    [InlineData(PropertyType.HouseApartment, PropertyTypeDto.HouseApartment)]
    public void Map_LargeEnum_MapsCorrectly(PropertyType input, PropertyTypeDto expected)
    {
        PropertyTypeDto result = _mappers.PropertyTypeMapper.Map(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(PropertyType.Invalid, PropertyTypeDto.Other)]
    [InlineData(PropertyType.Other, PropertyTypeDto.Other)]
    [InlineData(PropertyType.House, PropertyTypeDto.House)]
    [InlineData(PropertyType.RowHouse, PropertyTypeDto.RowHouse)]
    [InlineData(PropertyType.Apartment, PropertyTypeDto.Apartment)]
    [InlineData(PropertyType.Recreational, PropertyTypeDto.Recreational)]
    [InlineData(PropertyType.Cooperative, PropertyTypeDto.Cooperative)]
    [InlineData(PropertyType.Farm, PropertyTypeDto.Farm)]
    [InlineData(PropertyType.LandLeisure, PropertyTypeDto.LandLeisure)]
    [InlineData(PropertyType.LandResidence, PropertyTypeDto.LandResidence)]
    [InlineData(PropertyType.HouseApartment, PropertyTypeDto.HouseApartment)]
    public void ToExpression_LargeEnum_CompiledDelegate_MapsCorrectly(
        PropertyType input, PropertyTypeDto expected)
    {
        var expr = _mappers.PropertyTypeMapper.ToExpression();
        Func<PropertyType, PropertyTypeDto> func = expr.Compile();

        Assert.Equal(expected, func(input));
    }

    // -----------------------------------------------------------------------
    // Inlined into parent mapper — the Order mapper uses OrderStatus.Map(src.Status)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_EnumInlinedInParentMapper_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var order = new Order
        {
            Id       = 1,
            Status   = input,
            Customer = new Customer
            {
                Name = "Test",
                Email = "test@test.com",
            },
            Lines    = [],
        };

        OrderDto? dto = _mappers.Order.Map(order);

        Assert.Equal(expected, dto!.Status);
    }

    // -----------------------------------------------------------------------
    // Expression inlined in LINQ projection
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Project_EnumInlinedInProjection_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        IQueryable<Order> orders = new[] { new Order { Id = 1, Status = input, Customer = new() { Name = "Test", Email = "test@test.com" }, Lines = [] } }
            .AsQueryable();

        OrderDto result = orders.Project(_mappers.Order).Single();

        Assert.Equal(expected, result.Status);
    }
}
