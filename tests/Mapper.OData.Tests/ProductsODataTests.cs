using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebShop.OData.Projections;

namespace Mapper.OData.Tests;

/// <summary>
/// Integration tests verifying that mapper projections work correctly
/// when composed with OData query options ($filter, $orderby, $top, $skip, $count, $select).
/// </summary>
public sealed class ProductsODataTests : IClassFixture<WebShopODataFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public ProductsODataTests(WebShopODataFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsOnlyActiveProducts()
    {
        List<ProductProjection>? products = await GetODataValueAsync<List<ProductProjection>>("/odata/Products");

        Assert.NotNull(products);
        // 4 active products (laptop, phone, outOfStock, tShirt) — inactive is excluded
        Assert.Equal(4, products.Count);
        Assert.DoesNotContain(products, p => p.Name == "Discontinued Jacket");
    }

    [Fact]
    public async Task Get_ProjectsCategoryName()
    {
        List<ProductProjection>? products = await GetODataValueAsync<List<ProductProjection>>("/odata/Products");

        Assert.NotNull(products);
        ProductProjection laptop = Assert.Single(products, p => p.Id == TestIds.Laptop);
        Assert.Equal("Electronics", laptop.CategoryName);
    }

    [Fact]
    public async Task Get_ComputesIsAvailable()
    {
        List<ProductProjection>? products = await GetODataValueAsync<List<ProductProjection>>("/odata/Products");

        Assert.NotNull(products);

        ProductProjection laptop = Assert.Single(products, p => p.Id == TestIds.Laptop);
        Assert.True(laptop.IsAvailable);

        ProductProjection outOfStock = Assert.Single(products, p => p.Id == TestIds.OutOfStockProduct);
        Assert.False(outOfStock.IsAvailable);
    }

    [Fact]
    public async Task Filter_ByPrice_ReturnsMatchingProducts()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$filter=Price gt 100");

        Assert.NotNull(products);
        Assert.All(products, p => Assert.True(p.Price > 100));
        Assert.Contains(products, p => p.Name == "Laptop Pro");
        Assert.Contains(products, p => p.Name == "SmartPhone X");
        Assert.DoesNotContain(products, p => p.Name == "Basic T-Shirt");
    }

    [Fact]
    public async Task Filter_ByCategoryName_ReturnsMatchingProducts()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$filter=CategoryName eq 'Clothing'");

        Assert.NotNull(products);
        Assert.Single(products);
        Assert.Equal("Basic T-Shirt", products[0].Name);
    }

    [Fact]
    public async Task Filter_ByIsAvailable_ReturnsOnlyAvailableProducts()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$filter=IsAvailable eq true");

        Assert.NotNull(products);
        Assert.All(products, p => Assert.True(p.IsAvailable));
        Assert.DoesNotContain(products, p => p.Id == TestIds.OutOfStockProduct);
    }

    [Fact]
    public async Task OrderBy_Name_ReturnsSortedProducts()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$orderby=Name");

        Assert.NotNull(products);
        List<string> names = products.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public async Task OrderBy_PriceDesc_ReturnsSortedProducts()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$orderby=Price desc");

        Assert.NotNull(products);
        Assert.True(products.Count > 1);
        Assert.Equal(products.OrderByDescending(p => p.Price).Select(p => p.Id).ToList(),
                     products.Select(p => p.Id).ToList());
    }

    [Fact]
    public async Task Top_ReturnsLimitedResults()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$top=2");

        Assert.NotNull(products);
        Assert.Equal(2, products.Count);
    }

    [Fact]
    public async Task Skip_SkipsResults()
    {
        List<ProductProjection>? all =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$orderby=Name");
        List<ProductProjection>? skipped =
            await GetODataValueAsync<List<ProductProjection>>("/odata/Products?$orderby=Name&$skip=2");

        Assert.NotNull(all);
        Assert.NotNull(skipped);
        Assert.Equal(all.Count - 2, skipped.Count);
        Assert.Equal(all[2].Id, skipped[0].Id);
    }

    [Fact]
    public async Task Count_ReturnsCountInResponse()
    {
        HttpResponseMessage response = await _client.GetAsync("/odata/Products?$count=true");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(content);

        Assert.True(doc.RootElement.TryGetProperty("@odata.count", out JsonElement countElement));
        Assert.Equal(4, countElement.GetInt32());
    }

    [Fact]
    public async Task CombinedQuery_FilterOrderByTopSkip_WorksTogether()
    {
        List<ProductProjection>? products = await GetODataValueAsync<List<ProductProjection>>(
            "/odata/Products?$filter=Price gt 10&$orderby=Price desc&$top=2&$skip=1");

        Assert.NotNull(products);
        Assert.Equal(2, products.Count);
        Assert.True(products[0].Price >= products[1].Price);
        Assert.All(products, p => Assert.True(p.Price > 10));
    }

    [Fact]
    public async Task GetById_ReturnsSingleProduct()
    {
        List<ProductProjection>? products =
            await GetODataValueAsync<List<ProductProjection>>($"/odata/Products?$filter=Id eq {TestIds.Laptop}");

        Assert.NotNull(products);
        Assert.Single(products);
        Assert.Equal("Laptop Pro", products[0].Name);
        Assert.Equal(1299.99m, products[0].Price);
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
