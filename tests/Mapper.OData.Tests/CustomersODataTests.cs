using System.Net;
using System.Text.Json;
using WebShop.OData.Projections;

namespace Mapper.OData.Tests;

/// <summary>
/// Integration tests for the Customers OData endpoint — verifies that
/// computed aggregates (TotalOrders, TotalSpent) and flattened properties
/// work through the OData pipeline.
/// </summary>
public sealed class CustomersODataTests : IClassFixture<WebShopODataFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public CustomersODataTests(WebShopODataFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsAllCustomers()
    {
        List<CustomerProjection>? customers = await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers");

        Assert.NotNull(customers);
        Assert.Equal(2, customers.Count);
    }

    [Fact]
    public async Task Get_ComputesFullName()
    {
        List<CustomerProjection>? customers = await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers");

        Assert.NotNull(customers);
        Assert.Contains(customers, c => c.FullName == "Alice Smith");
        Assert.Contains(customers, c => c.FullName == "Bob Jones");
    }

    [Fact]
    public async Task Get_ComputesTotalOrders()
    {
        List<CustomerProjection>? customers = await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers");

        Assert.NotNull(customers);

        CustomerProjection alice = Assert.Single(customers, c => c.FullName == "Alice Smith");
        Assert.Equal(1, alice.TotalOrders);

        CustomerProjection bob = Assert.Single(customers, c => c.FullName == "Bob Jones");
        Assert.Equal(1, bob.TotalOrders);
    }

    [Fact]
    public async Task Get_ComputesTotalSpent()
    {
        List<CustomerProjection>? customers = await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers");

        Assert.NotNull(customers);

        CustomerProjection alice = Assert.Single(customers, c => c.FullName == "Alice Smith");
        // Alice's order: 1 x 1299.99 + 3 x 19.99 = 1299.99 + 59.97 = 1359.96
        Assert.Equal(1359.96m, alice.TotalSpent);

        CustomerProjection bob = Assert.Single(customers, c => c.FullName == "Bob Jones");
        // Bob's order: 2 x 899.00 = 1798.00
        Assert.Equal(1798.00m, bob.TotalSpent);
    }

    [Fact]
    public async Task Filter_ByFullName_ReturnsMatchingCustomer()
    {
        List<CustomerProjection>? customers =
            await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers?$filter=FullName eq 'Alice Smith'");

        Assert.NotNull(customers);
        Assert.Single(customers);
        Assert.Equal("alice@example.com", customers[0].Email);
    }

    [Fact]
    public async Task Filter_ByTotalSpent_ReturnsMatchingCustomers()
    {
        List<CustomerProjection>? customers =
            await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers?$filter=TotalSpent gt 1500");

        Assert.NotNull(customers);
        Assert.All(customers, c => Assert.True(c.TotalSpent > 1500));
    }

    [Fact]
    public async Task OrderBy_TotalSpentDesc_ReturnsSorted()
    {
        List<CustomerProjection>? customers =
            await GetODataValueAsync<List<CustomerProjection>>("/odata/Customers?$orderby=TotalSpent desc");

        Assert.NotNull(customers);
        Assert.Equal(2, customers.Count);
        Assert.True(customers[0].TotalSpent >= customers[1].TotalSpent);
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
