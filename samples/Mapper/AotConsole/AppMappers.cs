using ArchPillar.Extensions.Mapper;

namespace AotConsole;

/// <summary>
/// Partial MapperContext — the source generator detects this and emits
/// AOT-compatible mapping delegates that avoid Expression.Compile().
/// </summary>
public partial class AppMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }
    public Mapper<OrderItem, OrderItemDto> OrderItem { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        OrderStatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Processing => OrderStatusDto.Processing,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            OrderStatus.Delivered => OrderStatusDto.Delivered,
            _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
        });

        OrderItem = CreateMapper<OrderItem, OrderItemDto>(src => new OrderItemDto
        {
            ProductName = src.ProductName,
            Quantity = src.Quantity,
            UnitPrice = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id = src.Id,
            CustomerName = src.CustomerName,
            Total = src.Total,
            Status = OrderStatusMapper.Map(src.Status),
            IsAssignedToMe = src.AssignedToUserId == CurrentUserId,
            Items = src.Items.Project(OrderItem).ToList(),
        });

        // Wire AOT-generated delegates — replaces Expression.Compile()
        OnAotInitialize();
    }
}
