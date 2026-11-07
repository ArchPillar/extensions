using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies mappers reached through a compiler-generated closure: a local or a
/// method parameter, which is how a minimal API handler receives them. EF Core
/// lifts a closure into a query parameter, so such a mapper can only be resolved
/// before parameter extraction, by
/// <see cref="MapperEfCoreExtensions.ApplyMappers{TSource}"/>. Service provider
/// caching is off so every test starts from an empty enum mapping store; a table
/// left behind by another test would otherwise hide a closure that cannot be read.
/// </summary>
public sealed class CapturedMapperTests : IDisposable
{
    private readonly MapperInliningDbContext _db;

    public CapturedMapperTests()
    {
        DbContextOptionsBuilder<MapperInliningDbContext> builder = new DbContextOptionsBuilder<MapperInliningDbContext>()
            .UseSqlite("DataSource=:memory:")
            .EnableServiceProviderCaching(false);
        builder.UseArchPillarMapper();

        _db = new MapperInliningDbContext(builder.Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        SeedData(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task ApplyMappers_CapturedEnumMapper_TranslatesToFlatCaseAsync()
    {
        var mappers = new TestMappers();

        IQueryable<OrderStatusDto> query = _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => mappers.OrderStatusMapper.Map(o.Status))
            .ApplyMappers();

        Assert.Contains("CASE", query.ToQueryString(), StringComparison.Ordinal);
        Assert.Equal([OrderStatusDto.Pending, OrderStatusDto.Shipped], await query.ToListAsync());
    }

    [Fact]
    public async Task ApplyMappers_CapturedSymmetricEnumMapper_TranslatesBothDirectionsAsync()
    {
        var mappers = new SymmetricEnumTestMappers();

        IQueryable<OrderStatus> query = _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => mappers.OrderStatusMapper.MapReverse(mappers.OrderStatusMapper.Map(o.Status)))
            .ApplyMappers();

        Assert.Contains("CASE", query.ToQueryString(), StringComparison.Ordinal);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Shipped], await query.ToListAsync());
    }

    [Fact]
    public async Task ApplyMappers_CapturedMappersInMixedSelect_TranslatesToSqlAsync()
    {
        var mappers = new TestMappers();

        List<CapturedMapperRow> rows = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => new CapturedMapperRow
            {
                Id     = o.Id,
                Status = mappers.OrderStatusMapper.Map(o.Status),
                Order  = mappers.Order.Map(o),
                Lines  = o.Lines.Project(mappers.OrderLine).ToList(),
            })
            .ApplyMappers()
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(OrderStatusDto.Shipped, rows[1].Status);
        Assert.Equal(2, rows[1].Id);
        Assert.Equal(2, rows[0].Lines.Count);
        Assert.Equal(OrderStatusDto.Pending, rows[0].Order!.Status);
    }

    [Fact]
    public async Task ApplyMappers_CapturedMapperBesideCapturedValue_KeepsValueParameterizedAsync()
    {
        // One closure holds both the mapper and the filter value, as in a real handler. The second call
        // reuses the compiled query, so it only sees its own value if that value stayed a query parameter.
        var mappers = new TestMappers();

        List<OrderStatusDto> fromFirst = await StatusesFromAsync(mappers, minimumId: 1);
        List<OrderStatusDto> fromSecond = await StatusesFromAsync(mappers, minimumId: 2);

        Assert.Equal([OrderStatusDto.Pending, OrderStatusDto.Shipped], fromFirst);
        Assert.Equal([OrderStatusDto.Shipped], fromSecond);
    }

    [Fact]
    public async Task CapturedEnumMapper_WithoutApplyMappers_ThrowsHelpfulErrorAsync()
    {
        var mappers = new TestMappers();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Orders
                .Select(o => mappers.OrderStatusMapper.Map(o.Status))
                .ToListAsync());

        Assert.Contains("ApplyMappers", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CapturedMapper_WithoutApplyMappers_ThrowsHelpfulErrorAsync()
    {
        var mappers = new TestMappers();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Orders
                .Select(o => mappers.Order.Map(o))
                .ToListAsync());

        Assert.Contains("ApplyMappers", ex.Message, StringComparison.Ordinal);
    }

    private Task<List<OrderStatusDto>> StatusesFromAsync(TestMappers mappers, int minimumId)
    {
        return _db.Orders
            .Where(o => o.Id >= minimumId)
            .OrderBy(o => o.Id)
            .Select(o => mappers.OrderStatusMapper.Map(o.Status))
            .ApplyMappers()
            .ToListAsync();
    }

    private static void SeedData(MapperInliningDbContext db)
    {
        db.Orders.AddRange(
            new Order
            {
                Id = 1,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Pending,
                OwnerId = 10,
                Customer = new Customer { Name = "Alice", Email = "alice@example.com" },
                Lines =
                [
                    new OrderLine { Id = 1, ProductName = "Widget", Quantity = 2, UnitPrice = 9.99m, SupplierName = "Acme" },
                    new OrderLine { Id = 2, ProductName = "Gadget", Quantity = 1, UnitPrice = 19.99m, SupplierName = "Globex" },
                ],
            },
            new Order
            {
                Id = 2,
                CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.Shipped,
                OwnerId = 20,
                Customer = new Customer { Name = "Bob", Email = "bob@example.com" },
                Lines =
                [
                    new OrderLine { Id = 3, ProductName = "Sprocket", Quantity = 5, UnitPrice = 4.5m, SupplierName = "Initech" },
                ],
            });

        db.SaveChanges();
    }
}

internal sealed class CapturedMapperRow
{
    public int Id { get; set; }
    public OrderStatusDto Status { get; set; }
    public OrderDto? Order { get; set; }
    public List<OrderLineDto> Lines { get; set; } = [];
}
