using Npgsql;

namespace eShop.Ordering.API.Infrastructure.Services;

/// <summary>
/// The catalog's current price for each product, so an order is priced by the catalog
/// rather than by the unit prices the client sends.
/// </summary>
public interface ICatalogPrices
{
    Task<IReadOnlyDictionary<int, decimal>> GetPricesAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default);
}

public class CatalogPrices([FromKeyedServices("catalogdb")] NpgsqlDataSource catalogDb) : ICatalogPrices
{
    public async Task<IReadOnlyDictionary<int, decimal>> GetPricesAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToArray();
        var prices = new Dictionary<int, decimal>();
        if (ids.Length == 0)
        {
            return prices;
        }

        await using var command = catalogDb.CreateCommand("""SELECT "Id", "Price" FROM "Catalog" WHERE "Id" = ANY($1)""");
        command.Parameters.Add(new NpgsqlParameter { Value = ids });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            prices[reader.GetInt32(0)] = reader.GetDecimal(1);
        }

        return prices;
    }
}
