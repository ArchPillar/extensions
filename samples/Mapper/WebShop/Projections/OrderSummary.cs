using WebShop.Models;

namespace WebShop.Projections;

/// <summary>
/// Compact order summary assembled in a hand-written <c>Select</c> to showcase the
/// EF Core integration (<c>UseArchPillarMapper</c>). All three pieces are translated
/// server-side in a single query:
/// <list type="bullet">
/// <item><see cref="Status"/> — a direct <c>EnumMapper.Map()</c> call, emitted as a flat SQL <c>CASE</c>.</item>
/// <item><see cref="Customer"/> — a single property produced by a regular mapper, inlined into the query.</item>
/// <item><see cref="Lines"/> — a collection produced by <c>Project(mapper)</c>, inlined into the query.</item>
/// </list>
/// </summary>
public sealed class OrderSummary
{
    public Guid Id { get; set; }

    public OrderStatusDto Status { get; set; }

    public required CustomerProjection Customer { get; set; }

    public required List<OrderLineProjection> Lines { get; set; }
}
