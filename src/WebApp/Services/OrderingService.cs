namespace eShop.WebApp.Services;

public class OrderingService(HttpClient httpClient)
{
    private readonly string remoteServiceBaseUrl = "/api/Orders/";

    public Task<OrderRecord[]> GetOrders()
    {
        return httpClient.GetFromJsonAsync<OrderRecord[]>(remoteServiceBaseUrl)!;
    }

    public async Task CancelOrder(int orderNumber)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, remoteServiceBaseUrl + "cancel");
        request.Headers.Add("x-requestid", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new { OrderNumber = orderNumber });
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public Task CreateOrder(CreateOrderRequest request, Guid requestId)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, remoteServiceBaseUrl);
        requestMessage.Headers.Add("x-requestid", requestId.ToString());
        requestMessage.Content = JsonContent.Create(request);
        return httpClient.SendAsync(requestMessage);
    }
}

public record OrderRecord(
    int OrderNumber,
    DateTime Date,
    string Status,
    decimal Total,
    string? Description = null,
    DateTimeOffset? ItemReservationExpiresAt = null,
    bool ItemReservationCancellationRequested = false);
