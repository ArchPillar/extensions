using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper;

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

    // -----------------------------------------------------------------------
    // Nullable enum — standalone Map(TSource?) → TDest?
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NullableSource_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        OrderStatusDto? result = _mappers.OrderStatusMapper.Map((OrderStatus?)input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Map_NullableSource_Null_ReturnsNull()
    {
        OrderStatusDto? result = _mappers.OrderStatusMapper.Map((OrderStatus?)null);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Nullable enum — standalone Map(TSource?, TDest defaultValue) → TDest
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NullableSourceWithDefault_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        OrderStatusDto result = _mappers.OrderStatusMapper.Map((OrderStatus?)input, OrderStatusDto.Pending);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Map_NullableSourceWithDefault_Null_ReturnsDefault()
    {
        OrderStatusDto result = _mappers.OrderStatusMapper.Map((OrderStatus?)null, OrderStatusDto.Shipped);

        Assert.Equal(OrderStatusDto.Shipped, result);
    }

    // -----------------------------------------------------------------------
    // Nullable enum — ToNullableExpression
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void ToNullableExpression_NonNullValue_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        Expression<Func<OrderStatus?, OrderStatusDto?>> expr = _mappers.OrderStatusMapper.ToNullableExpression();
        Func<OrderStatus?, OrderStatusDto?> func = expr.Compile();

        Assert.Equal(expected, func(input));
    }

    [Fact]
    public void ToNullableExpression_NullValue_ReturnsNull()
    {
        Expression<Func<OrderStatus?, OrderStatusDto?>> expr = _mappers.OrderStatusMapper.ToNullableExpression();
        Func<OrderStatus?, OrderStatusDto?> func = expr.Compile();

        Assert.Null(func(null));
    }
}

// ---------------------------------------------------------------------------
// Nullable enum mapping — inlined in parent mapper
// ---------------------------------------------------------------------------

public class NullableEnumMappingTests
{
    private readonly NullableEnumMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Nullable → Nullable (Map(TSource?) → TDest?)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NullableToNullable_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = input };

        OrderDtoWithNullableStatus? result = _mappers.NullableToNullable.Map(source);

        Assert.Equal(expected, result!.Status);
    }

    [Fact]
    public void Map_NullableToNullable_Null_ReturnsNull()
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = null };

        OrderDtoWithNullableStatus? result = _mappers.NullableToNullable.Map(source);

        Assert.Null(result!.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Project_NullableToNullable_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = input } }.AsQueryable();

        OrderDtoWithNullableStatus result = source.Project(_mappers.NullableToNullable).Single();

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Project_NullableToNullable_Null_ReturnsNull()
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = null } }.AsQueryable();

        OrderDtoWithNullableStatus result = source.Project(_mappers.NullableToNullable).Single();

        Assert.Null(result.Status);
    }

    // -----------------------------------------------------------------------
    // Nullable → Non-nullable with default (Map(TSource?, TDest) → TDest)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NullableToNonNullable_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = input };

        OrderDtoWithDefaultStatus? result = _mappers.NullableToNonNullable.Map(source);

        Assert.Equal(expected, result!.Status);
    }

    [Fact]
    public void Map_NullableToNonNullable_Null_ReturnsDefault()
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = null };

        OrderDtoWithDefaultStatus? result = _mappers.NullableToNonNullable.Map(source);

        Assert.Equal(OrderStatusDto.Pending, result!.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Project_NullableToNonNullable_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = input } }.AsQueryable();

        OrderDtoWithDefaultStatus result = source.Project(_mappers.NullableToNonNullable).Single();

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Project_NullableToNonNullable_Null_ReturnsDefault()
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = null } }.AsQueryable();

        OrderDtoWithDefaultStatus result = source.Project(_mappers.NullableToNonNullable).Single();

        Assert.Equal(OrderStatusDto.Pending, result.Status);
    }

    // -----------------------------------------------------------------------
    // Non-nullable → Nullable (implicit lift)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NonNullableToNullable_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var source = new Order
        {
            Id       = 1,
            Status   = input,
            Customer = new Customer { Name = "Test", Email = "test@test.com" },
            Lines    = [],
        };

        OrderDtoWithNullableStatus? result = _mappers.NonNullableToNullable.Map(source);

        Assert.Equal(expected, result!.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Project_NonNullableToNullable_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        IQueryable<Order> source = new[]
        {
            new Order
            {
                Id       = 1,
                Status   = input,
                Customer = new Customer { Name = "Test", Email = "test@test.com" },
                Lines    = [],
            },
        }.AsQueryable();

        OrderDtoWithNullableStatus result = source.Project(_mappers.NonNullableToNullable).Single();

        Assert.Equal(expected, result.Status);
    }
}
