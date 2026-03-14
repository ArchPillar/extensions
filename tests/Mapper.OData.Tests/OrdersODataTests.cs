using System.Net;
using System.Text.Json;
using WebShop.OData.Projections;

namespace Mapper.OData.Tests;

/// <summary>
/// Integration tests for the Orders OData endpoint — verifies that computed
/// totals, flattened customer names, and nested line collections all work
/// through the OData pipeline with mapper projections.
/// </summary>
public sealed class OrdersODataTests : IClassFixture<WebShopODataFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public OrdersODataTests(WebShopODataFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsAllOrders()
    {
        List<OrderProjection>? orders = await GetODataValueAsync<List<OrderProjection>>("/odata/Orders");

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
    }

    [Fact]
    public async Task Get_ComputesTotalAmount()
    {
        List<OrderProjection>? orders = await GetODataValueAsync<List<OrderProjection>>("/odata/Orders");

        Assert.NotNull(orders);

        OrderProjection aliceOrder = Assert.Single(orders, o => o.Id == TestIds.AliceOrder);
        // 1 x 1299.99 + 3 x 19.99 = 1359.96
        Assert.Equal(1359.96m, aliceOrder.TotalAmount);

        OrderProjection bobOrder = Assert.Single(orders, o => o.Id == TestIds.BobOrder);
        // 2 x 899.00 = 1798.00
        Assert.Equal(1798.00m, bobOrder.TotalAmount);
    }

    [Fact]
    public async Task Get_ComputesLineCount()
    {
        List<OrderProjection>? orders = await GetODataValueAsync<List<OrderProjection>>("/odata/Orders");

        Assert.NotNull(orders);

        OrderProjection aliceOrder = Assert.Single(orders, o => o.Id == TestIds.AliceOrder);
        Assert.Equal(2, aliceOrder.LineCount);

        OrderProjection bobOrder = Assert.Single(orders, o => o.Id == TestIds.BobOrder);
        Assert.Equal(1, bobOrder.LineCount);
    }

    [Fact]
    public async Task Get_FlattensCustomerFullName()
    {
        List<OrderProjection>? orders = await GetODataValueAsync<List<OrderProjection>>("/odata/Orders");

        Assert.NotNull(orders);

        OrderProjection aliceOrder = Assert.Single(orders, o => o.Id == TestIds.AliceOrder);
        Assert.Equal("Alice Smith", aliceOrder.CustomerFullName);

        OrderProjection bobOrder = Assert.Single(orders, o => o.Id == TestIds.BobOrder);
        Assert.Equal("Bob Jones", bobOrder.CustomerFullName);
    }

    [Fact]
    public async Task Get_StatusRenderedAsString()
    {
        List<OrderProjection>? orders = await GetODataValueAsync<List<OrderProjection>>("/odata/Orders");

        Assert.NotNull(orders);

        OrderProjection aliceOrder = Assert.Single(orders, o => o.Id == TestIds.AliceOrder);
        Assert.Equal("Delivered", aliceOrder.Status);

        OrderProjection bobOrder = Assert.Single(orders, o => o.Id == TestIds.BobOrder);
        Assert.Equal("Pending", bobOrder.Status);
    }

    [Fact]
    public async Task Filter_ByStatus_ReturnsMatchingOrders()
    {
        List<OrderProjection>? orders =
            await GetODataValueAsync<List<OrderProjection>>("/odata/Orders?$filter=Status eq 'Pending'");

        Assert.NotNull(orders);
        Assert.Single(orders);
        Assert.Equal(TestIds.BobOrder, orders[0].Id);
    }

    [Fact]
    public async Task Filter_ByTotalAmount_ReturnsMatchingOrders()
    {
        List<OrderProjection>? orders =
            await GetODataValueAsync<List<OrderProjection>>("/odata/Orders?$filter=TotalAmount gt 1500");

        Assert.NotNull(orders);
        Assert.All(orders, o => Assert.True(o.TotalAmount > 1500));
    }

    [Fact]
    public async Task Filter_ByCustomerFullName_ReturnsMatchingOrders()
    {
        List<OrderProjection>? orders =
            await GetODataValueAsync<List<OrderProjection>>("/odata/Orders?$filter=CustomerFullName eq 'Alice Smith'");

        Assert.NotNull(orders);
        Assert.Single(orders);
        Assert.Equal(TestIds.AliceOrder, orders[0].Id);
    }

    [Fact]
    public async Task OrderBy_TotalAmountDesc_ReturnsSorted()
    {
        List<OrderProjection>? orders =
            await GetODataValueAsync<List<OrderProjection>>("/odata/Orders?$orderby=TotalAmount desc");

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.True(orders[0].TotalAmount >= orders[1].TotalAmount);
    }

    [Fact]
    public async Task OrderBy_PlacedAt_ReturnsSorted()
    {
        List<OrderProjection>? orders =
            await GetODataValueAsync<List<OrderProjection>>("/odata/Orders?$orderby=PlacedAt");

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.True(orders[0].PlacedAt <= orders[1].PlacedAt);
    }

    private async Task<T?> GetODataValueAsync<T>(string requestUri)
    {
        HttpResponseMessage response = await _client.GetAsync(requestUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("value", out JsonElement value))
        {
            return JsonSerializer.Deserialize<T>(value.GetRawText(), JsonOptions);
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }
}
