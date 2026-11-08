using System.Text.Json.Serialization;

namespace eShop.Basket.API.Services;

public class CatalogClient(HttpClient httpClient)
{
    public async Task<HashSet<int>> GetProductIdsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var query = string.Join("&", productIds.Select(id => $"ids={id}"));
        var products = await httpClient.GetFromJsonAsync(
            $"/api/catalog/items/by?api-version=2.0&{query}",
            CatalogJsonContext.Default.CatalogProductArray,
            cancellationToken);

        return products?.Select(product => product.Id).ToHashSet()
            ?? throw new JsonException("Catalog returned a null product list.");
    }
}

public record CatalogProduct(int Id);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CatalogProduct[]))]
internal partial class CatalogJsonContext : JsonSerializerContext;
