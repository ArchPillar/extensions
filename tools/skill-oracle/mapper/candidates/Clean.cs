// SKILL-GENERATED (archpillar-mapper). Scenario: canonical entity->DTO with a nested
// collection, an optional/expensive join, a runtime-variable computed flag, and an enum->enum,
// projected via EF Core. Regenerate by re-running this scenario through an agent WITH the skill.
using ArchPillar.Extensions.Mapper;
using Microsoft.EntityFrameworkCore;
namespace MapTest.Clean;

public sealed class AppMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();
    public SymmetricEnumMapper<OrderStatus, OrderStatusDto> Status { get; }
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        Status = CreateSymmetricEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending   => OrderStatusDto.Pending,
            OrderStatus.Shipped   => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
        });

        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            Id          = src.Id,
            ProductName = src.Product.Name,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id     = src.Id,
            Status = Status.Map(src.Status),
            Lines  = src.Lines.Project(OrderLine).ToList(),
            IsOwner = src.OwnerId == CurrentUserId,
        })
        .Optional(dest => dest.CustomerName, src => src.Customer.Name);

        EagerBuildAll();
    }
}

public static class CleanQuery
{
    public static async Task<List<OrderDto>> GetActiveOrdersAsync(
        AppDbContext db, AppMappers mappers, int currentUserId)
    {
        return await db.Orders
            .Where(o => o.IsActive)
            .Project(mappers.Order, o => o
                .Include(m => m.CustomerName)
                .Set(mappers.CurrentUserId, currentUserId))
            .ToListAsync();
    }
}
