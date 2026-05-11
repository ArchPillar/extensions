using System.Net;
using System.Text.Json;
using WebShop.OData.Projections;

namespace WebShop.OData;

/// <summary>
/// Integration tests for the Categories OData endpoint — verifies that
/// computed properties like ProductCount work through the OData pipeline.
/// </summary>
public sealed class CategoriesODataTests : IClassFixture<WebShopODataFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public CategoriesODataTests(WebShopODataFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsAllCategories()
    {
        List<CategoryProjection>? categories = await GetODataValueAsync<List<CategoryProjection>>("/odata/Categories");

        Assert.NotNull(categories);
        Assert.Equal(2, categories.Count);
    }

    [Fact]
    public async Task Get_ComputesProductCount()
    {
        List<CategoryProjection>? categories = await GetODataValueAsync<List<CategoryProjection>>("/odata/Categories");

        Assert.NotNull(categories);

        CategoryProjection electronics = Assert.Single(categories, c => c.Name == "Electronics");
        Assert.Equal(3, electronics.ProductCount); // laptop, phone, outOfStock (excludes inactive)

        CategoryProjection clothing = Assert.Single(categories, c => c.Name == "Clothing");
        Assert.Equal(2, clothing.ProductCount); // tShirt + inactive jacket (ProductCount counts all, not just active)
    }

    [Fact]
    public async Task Filter_ByName_ReturnsMatchingCategory()
    {
        List<CategoryProjection>? categories =
            await GetODataValueAsync<List<CategoryProjection>>("/odata/Categories?$filter=Name eq 'Electronics'");

        Assert.NotNull(categories);
        Assert.Single(categories);
        Assert.Equal("Electronics", categories[0].Name);
    }

    [Fact]
    public async Task Filter_ByProductCount_ReturnsMatchingCategories()
    {
        List<CategoryProjection>? categories =
            await GetODataValueAsync<List<CategoryProjection>>("/odata/Categories?$filter=ProductCount ge 3");

        Assert.NotNull(categories);
        Assert.All(categories, c => Assert.True(c.ProductCount >= 3));
    }

    [Fact]
    public async Task OrderBy_ProductCount_ReturnsSorted()
    {
        List<CategoryProjection>? categories =
            await GetODataValueAsync<List<CategoryProjection>>("/odata/Categories?$orderby=ProductCount desc");

        Assert.NotNull(categories);
        Assert.True(categories[0].ProductCount >= categories[1].ProductCount);
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
