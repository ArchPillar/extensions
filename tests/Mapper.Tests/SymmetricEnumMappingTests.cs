using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Tests;

// ---------------------------------------------------------------------------
// Test mapper contexts for symmetric enum mapping
// ---------------------------------------------------------------------------

/// <summary>
/// Uses <see cref="SymmetricEnumMapper{TLeft,TRight}"/> for 1:1 enum mappings.
/// </summary>
public class SymmetricEnumTestMappers : MapperContext
{
    public SymmetricEnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }
    public SymmetricEnumMapper<UserRole, UserRoleDto> UserRoleMapper { get; }

    // Parent mappers that inline the symmetric enum mapper
    public Mapper<Order, OrderDto> Order { get; }
    public Mapper<OrderDto, Order> OrderReverse { get; }

    public SymmetricEnumTestMappers()
    {
        OrderStatusMapper = CreateSymmetricEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        });

        UserRoleMapper = CreateSymmetricEnumMapper<UserRole, UserRoleDto>(r => r switch
        {
            UserRole.Guest => UserRoleDto.Guest,
            UserRole.Member => UserRoleDto.Member,
            UserRole.Admin => UserRoleDto.Admin,
            _ => throw new ArgumentOutOfRangeException(nameof(r)),
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id = src.Id,
            PlacedAt = src.CreatedAt,
            Status = OrderStatusMapper.Map(src.Status),
            IsOwner = false,
            Lines = new List<OrderLineDto>(),
        })
        .Ignore(dest => dest.CustomerName);

        OrderReverse = CreateMapper<OrderDto, Order>(dto => new Order
        {
            Id = dto.Id,
            CreatedAt = dto.PlacedAt,
            Status = OrderStatusMapper.MapReverse(dto.Status),
            OwnerId = 0,
            Customer = new Customer { Name = "", Email = "" },
            Lines = new List<OrderLine>(),
        });
    }
}

/// <summary>
/// Eager-build variant — validates <see cref="MapperContext.EagerBuildAll"/>
/// discovers <see cref="SymmetricEnumMapper{TLeft,TRight}"/> properties.
/// </summary>
public class EagerSymmetricEnumTestMappers : SymmetricEnumTestMappers
{
    public EagerSymmetricEnumTestMappers() { EagerBuildAll(); }
}

/// <summary>
/// Nullable enum scenarios with symmetric mapper.
/// </summary>
public class SymmetricNullableEnumMappers : MapperContext
{
    public SymmetricEnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }

    /// <summary>Nullable → nullable forward: <c>Map(TLeft?)</c> → <c>TRight?</c>.</summary>
    public Mapper<OrderWithNullableStatus, OrderDtoWithNullableStatus> NullableForward { get; }

    /// <summary>Nullable → nullable reverse: <c>MapReverse(TRight?)</c> → <c>TLeft?</c>.</summary>
    public Mapper<OrderDtoWithNullableStatus, OrderWithNullableStatus> NullableReverse { get; }

    /// <summary>Nullable → non-nullable with default forward.</summary>
    public Mapper<OrderWithNullableStatus, OrderDtoWithDefaultStatus> NullableToNonNullableForward { get; }

    public SymmetricNullableEnumMappers()
    {
        OrderStatusMapper = CreateSymmetricEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        });

        NullableForward = CreateMapper<OrderWithNullableStatus, OrderDtoWithNullableStatus>(
            src => new OrderDtoWithNullableStatus
            {
                Id     = src.Id,
                Status = OrderStatusMapper.Map(src.Status),
            });

        NullableReverse = CreateMapper<OrderDtoWithNullableStatus, OrderWithNullableStatus>(
            dto => new OrderWithNullableStatus
            {
                Id     = dto.Id,
                Status = OrderStatusMapper.MapReverse(dto.Status),
            });

        NullableToNonNullableForward = CreateMapper<OrderWithNullableStatus, OrderDtoWithDefaultStatus>(
            src => new OrderDtoWithDefaultStatus
            {
                Id     = src.Id,
                Status = OrderStatusMapper.Map(src.Status, OrderStatusDto.Pending),
            });
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class SymmetricEnumMappingTests
{
    private readonly SymmetricEnumTestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Standalone forward: Map(TLeft) → TRight
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_Forward_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        OrderStatusDto result = _mappers.OrderStatusMapper.Map(input);

        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // Standalone reverse: MapReverse(TRight) → TLeft
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    [InlineData(OrderStatusDto.Cancelled, OrderStatus.Cancelled)]
    public void MapReverse_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        OrderStatus result = _mappers.OrderStatusMapper.MapReverse(input);

        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // Forward expression tree
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void ToExpression_Forward_CompiledDelegate_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var expr = _mappers.OrderStatusMapper.ToExpression();
        var func = expr.Compile();

        Assert.Equal(expected, func(input));
    }

    // -----------------------------------------------------------------------
    // Reverse expression tree
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    [InlineData(OrderStatusDto.Cancelled, OrderStatus.Cancelled)]
    public void ToReverseExpression_CompiledDelegate_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        Expression<Func<OrderStatusDto, OrderStatus>> expr = _mappers.OrderStatusMapper.ToReverseExpression();
        Func<OrderStatusDto, OrderStatus> func = expr.Compile();

        Assert.Equal(expected, func(input));
    }

    // -----------------------------------------------------------------------
    // Inner mapper access
    // -----------------------------------------------------------------------

    [Fact]
    public void Forward_ReturnsWorkingEnumMapper()
    {
        EnumMapper<OrderStatus, OrderStatusDto> forward = _mappers.OrderStatusMapper.Forward;

        Assert.Equal(OrderStatusDto.Shipped, forward.Map(OrderStatus.Shipped));
    }

    [Fact]
    public void Reverse_ReturnsWorkingEnumMapper()
    {
        EnumMapper<OrderStatusDto, OrderStatus> reverse = _mappers.OrderStatusMapper.Reverse;

        Assert.Equal(OrderStatus.Shipped, reverse.Map(OrderStatusDto.Shipped));
    }

    // -----------------------------------------------------------------------
    // Inlined in parent mapper — forward (Map)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_ForwardInlinedInParentMapper_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var order = new Order
        {
            Id       = 1,
            Status   = input,
            Customer = new Customer { Name = "Test", Email = "test@test.com" },
            Lines    = [],
        };

        OrderDto? dto = _mappers.Order.Map(order);

        Assert.Equal(expected, dto!.Status);
    }

    // -----------------------------------------------------------------------
    // Inlined in parent mapper — reverse (MapReverse)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    [InlineData(OrderStatusDto.Cancelled, OrderStatus.Cancelled)]
    public void MapReverse_InlinedInParentMapper_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        var dto = new OrderDto
        {
            Id       = 1,
            PlacedAt = DateTime.UtcNow,
            Status   = input,
            IsOwner  = false,
            Lines    = [],
        };

        Order? order = _mappers.OrderReverse.Map(dto);

        Assert.Equal(expected, order!.Status);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — forward
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Project_ForwardInlinedInProjection_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        IQueryable<Order> orders = new[]
        {
            new Order
            {
                Id = 1, Status = input,
                Customer = new Customer { Name = "Test", Email = "test@test.com" },
                Lines = [],
            },
        }.AsQueryable();

        OrderDto result = orders.Project(_mappers.Order).Single();

        Assert.Equal(expected, result.Status);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — reverse
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    [InlineData(OrderStatusDto.Cancelled, OrderStatus.Cancelled)]
    public void Project_ReverseInlinedInProjection_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        IQueryable<OrderDto> dtos = new[]
        {
            new OrderDto
            {
                Id = 1, PlacedAt = DateTime.UtcNow, Status = input,
                IsOwner = false, Lines = [],
            },
        }.AsQueryable();

        Order result = dtos.Project(_mappers.OrderReverse).Single();

        Assert.Equal(expected, result.Status);
    }

    // -----------------------------------------------------------------------
    // Nullable forward: Map(TLeft?) → TRight?
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NullableForward_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        OrderStatusDto? result = _mappers.OrderStatusMapper.Map((OrderStatus?)input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Map_NullableForward_Null_ReturnsNull()
    {
        OrderStatusDto? result = _mappers.OrderStatusMapper.Map((OrderStatus?)null);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Nullable reverse: MapReverse(TRight?) → TLeft?
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    [InlineData(OrderStatusDto.Cancelled, OrderStatus.Cancelled)]
    public void MapReverse_Nullable_NonNull_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        OrderStatus? result = _mappers.OrderStatusMapper.MapReverse((OrderStatusDto?)input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MapReverse_Nullable_Null_ReturnsNull()
    {
        OrderStatus? result = _mappers.OrderStatusMapper.MapReverse((OrderStatusDto?)null);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Nullable forward with default: Map(TLeft?, TRight) → TRight
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NullableForwardWithDefault_Null_ReturnsDefault()
    {
        OrderStatusDto result = _mappers.OrderStatusMapper.Map((OrderStatus?)null, OrderStatusDto.Shipped);

        Assert.Equal(OrderStatusDto.Shipped, result);
    }

    // -----------------------------------------------------------------------
    // Nullable reverse with default: MapReverse(TRight?, TLeft) → TLeft
    // -----------------------------------------------------------------------

    [Fact]
    public void MapReverse_NullableWithDefault_Null_ReturnsDefault()
    {
        OrderStatus result = _mappers.OrderStatusMapper.MapReverse((OrderStatusDto?)null, OrderStatus.Shipped);

        Assert.Equal(OrderStatus.Shipped, result);
    }

    // -----------------------------------------------------------------------
    // Nullable expression trees
    // -----------------------------------------------------------------------

    [Fact]
    public void ToNullableExpression_Forward_NullValue_ReturnsNull()
    {
        Expression<Func<OrderStatus?, OrderStatusDto?>> expr = _mappers.OrderStatusMapper.ToNullableExpression();
        Func<OrderStatus?, OrderStatusDto?> func = expr.Compile();

        Assert.Null(func(null));
    }

    [Fact]
    public void ToReverseNullableExpression_NullValue_ReturnsNull()
    {
        Expression<Func<OrderStatusDto?, OrderStatus?>> expr = _mappers.OrderStatusMapper.ToReverseNullableExpression();
        Func<OrderStatusDto?, OrderStatus?> func = expr.Compile();

        Assert.Null(func(null));
    }

    // -----------------------------------------------------------------------
    // Bijection validation — many-to-one must throw
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateSymmetricEnumMapper_NonBijective_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new NonBijectiveMappers();
        });

        Assert.Contains("one-to-one", exception.Message);
        Assert.Contains("Invalid", exception.Message);
        Assert.Contains("Other", exception.Message);
    }

    // -----------------------------------------------------------------------
    // EagerBuildAll discovers SymmetricEnumMapper
    // -----------------------------------------------------------------------

    [Fact]
    public void EagerBuildAll_DiscoversSymmetricEnumMapper()
    {
        // If this throws, EagerBuildAll didn't compile the symmetric mapper.
        var mappers = new EagerSymmetricEnumTestMappers();

        Assert.Equal(OrderStatusDto.Shipped, mappers.OrderStatusMapper.Map(OrderStatus.Shipped));
        Assert.Equal(OrderStatus.Shipped, mappers.OrderStatusMapper.MapReverse(OrderStatusDto.Shipped));
    }
}

// ---------------------------------------------------------------------------
// Nullable enum mapping — inlined in parent mapper (symmetric)
// ---------------------------------------------------------------------------

public class SymmetricNullableEnumMappingTests
{
    private readonly SymmetricNullableEnumMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Nullable → Nullable forward (Map)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatusDto.Cancelled)]
    public void Map_NullableForward_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = input };

        OrderDtoWithNullableStatus? result = _mappers.NullableForward.Map(source);

        Assert.Equal(expected, result!.Status);
    }

    [Fact]
    public void Map_NullableForward_Null_ReturnsNull()
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = null };

        OrderDtoWithNullableStatus? result = _mappers.NullableForward.Map(source);

        Assert.Null(result!.Status);
    }

    // -----------------------------------------------------------------------
    // Nullable → Nullable reverse (MapReverse)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    [InlineData(OrderStatusDto.Cancelled, OrderStatus.Cancelled)]
    public void MapReverse_NullableReverse_NonNull_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        var source = new OrderDtoWithNullableStatus { Id = 1, Status = input };

        OrderWithNullableStatus? result = _mappers.NullableReverse.Map(source);

        Assert.Equal(expected, result!.Status);
    }

    [Fact]
    public void MapReverse_NullableReverse_Null_ReturnsNull()
    {
        var source = new OrderDtoWithNullableStatus { Id = 1, Status = null };

        OrderWithNullableStatus? result = _mappers.NullableReverse.Map(source);

        Assert.Null(result!.Status);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — nullable forward
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatusDto.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatusDto.Shipped)]
    public void Project_NullableForward_NonNull_MapsCorrectly(OrderStatus input, OrderStatusDto expected)
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = input } }.AsQueryable();

        OrderDtoWithNullableStatus result = source.Project(_mappers.NullableForward).Single();

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Project_NullableForward_Null_ReturnsNull()
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = null } }.AsQueryable();

        OrderDtoWithNullableStatus result = source.Project(_mappers.NullableForward).Single();

        Assert.Null(result.Status);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — nullable reverse
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatusDto.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatusDto.Shipped, OrderStatus.Shipped)]
    public void Project_NullableReverse_NonNull_MapsCorrectly(OrderStatusDto input, OrderStatus expected)
    {
        IQueryable<OrderDtoWithNullableStatus> source =
            new[] { new OrderDtoWithNullableStatus { Id = 1, Status = input } }.AsQueryable();

        OrderWithNullableStatus result = source.Project(_mappers.NullableReverse).Single();

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Project_NullableReverse_Null_ReturnsNull()
    {
        IQueryable<OrderDtoWithNullableStatus> source =
            new[] { new OrderDtoWithNullableStatus { Id = 1, Status = null } }.AsQueryable();

        OrderWithNullableStatus result = source.Project(_mappers.NullableReverse).Single();

        Assert.Null(result.Status);
    }

    // -----------------------------------------------------------------------
    // Nullable → Non-nullable with default forward
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NullableToNonNullableForward_Null_ReturnsDefault()
    {
        var source = new OrderWithNullableStatus { Id = 1, Status = null };

        OrderDtoWithDefaultStatus? result = _mappers.NullableToNonNullableForward.Map(source);

        Assert.Equal(OrderStatusDto.Pending, result!.Status);
    }

    [Fact]
    public void Project_NullableToNonNullableForward_Null_ReturnsDefault()
    {
        IQueryable<OrderWithNullableStatus> source =
            new[] { new OrderWithNullableStatus { Id = 1, Status = null } }.AsQueryable();

        OrderDtoWithDefaultStatus result = source.Project(_mappers.NullableToNonNullableForward).Single();

        Assert.Equal(OrderStatusDto.Pending, result.Status);
    }
}

// ---------------------------------------------------------------------------
// Helper: context that attempts a non-bijective symmetric mapping
// ---------------------------------------------------------------------------

internal class NonBijectiveMappers : MapperContext
{
    public SymmetricEnumMapper<PropertyType, PropertyTypeDto> PropertyTypeMapper { get; }

    public NonBijectiveMappers()
    {
        // PropertyType.Invalid and PropertyType.Other both map to PropertyTypeDto.Other
        // — this is not bijective and must throw.
        PropertyTypeMapper = CreateSymmetricEnumMapper<PropertyType, PropertyTypeDto>(t => t switch
        {
            PropertyType.Invalid => PropertyTypeDto.Other,
            PropertyType.Other => PropertyTypeDto.Other,
            PropertyType.House => PropertyTypeDto.House,
            PropertyType.RowHouse => PropertyTypeDto.RowHouse,
            PropertyType.Apartment => PropertyTypeDto.Apartment,
            PropertyType.Recreational => PropertyTypeDto.Recreational,
            PropertyType.Cooperative => PropertyTypeDto.Cooperative,
            PropertyType.Farm => PropertyTypeDto.Farm,
            PropertyType.LandLeisure => PropertyTypeDto.LandLeisure,
            PropertyType.LandResidence => PropertyTypeDto.LandResidence,
            PropertyType.HouseApartment => PropertyTypeDto.HouseApartment,
            _ => throw new ArgumentOutOfRangeException(nameof(t)),
        });
    }
}
