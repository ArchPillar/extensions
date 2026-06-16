// SKILL-GENERATED (archpillar-mapper). Scenario: in-place update of a tracked entity and
// its keyed child collection (MapTo + DeepWithIdentity via MapToCollection).
using ArchPillar.Extensions.Mapper;
namespace MapTest.MapToScenario;

public class LineEntity  { public int Id { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } }
public class OrderEntity { public int Id { get; set; } public string Notes { get; set; } = ""; public List<LineEntity> Lines { get; set; } = new(); }
public class LineUpdateCommand  { public int Id { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } }
public class OrderUpdateCommand { public int Id { get; set; } public string Notes { get; set; } = ""; public List<LineUpdateCommand> Lines { get; set; } = new(); }

public class OrderMappers : MapperContext
{
    public Mapper<LineUpdateCommand, LineEntity> Line { get; }
    public Mapper<OrderUpdateCommand, OrderEntity> Order { get; }
    public OrderMappers()
    {
        Line = CreateMapper<LineUpdateCommand, LineEntity>(src => new LineEntity
        { Id = src.Id, Quantity = src.Quantity, UnitPrice = src.UnitPrice });

        Order = CreateMapper<OrderUpdateCommand, OrderEntity>(src => new OrderEntity
        { Id = src.Id, Notes = src.Notes, Lines = src.Lines.Project(Line).ToList() })
        .MapToCollection(dest => dest.Lines, src => src.Lines, Line, src => src.Id, dest => dest.Id);

        EagerBuildAll();
    }
}

public static class OrderUpdater
{
    public static void Apply(OrderMappers mappers, OrderEntity tracked, OrderUpdateCommand command)
        => mappers.Order.MapTo(command, tracked);
}
