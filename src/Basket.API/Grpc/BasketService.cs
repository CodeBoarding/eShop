using System.Diagnostics.CodeAnalysis;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Extensions;
using eShop.Basket.API.Model;
using eShop.Basket.API.Services;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace eShop.Basket.API.Grpc;

public class BasketService(
    IBasketRepository repository,
    ILogger<BasketService> logger,
    CatalogClient catalog) : Basket.BasketBase
{
    [AllowAnonymous]
    public override async Task<CustomerBasketResponse> GetBasket(GetBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            return new();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin GetBasketById call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var data = await repository.GetBasketAsync(userId);

        if (data is not null)
        {
            return MapToCustomerBasketResponse(data);
        }

        return new();
    }

    public override async Task<CustomerBasketResponse> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin UpdateBasket call from method {Method} for basket id {Id}", context.Method, userId);
        }

        // Bound the batch lookup and reject ambiguous or invalid basket lines before any I/O.
        var productIds = request.Items.Select(item => item.ProductId).ToArray();
        if (request.Items.Count > 100 ||
            request.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0) ||
            productIds.Distinct().Count() != productIds.Length)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "A basket supports up to 100 unique products with positive IDs and quantities."));
        }

        // Clearing a basket should remain possible even when Catalog is unavailable.
        if (productIds.Length > 0)
        {
            HashSet<int> existingIds;
            try
            {
                existingIds = await catalog.GetProductIdsAsync(productIds, context.CancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or
                TimeoutRejectedException or BrokenCircuitException ||
                exception is OperationCanceledException && !context.CancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Catalog lookup failed while updating a basket");
                throw new RpcException(new Status(StatusCode.Unavailable,
                    "Catalog is temporarily unavailable. Your basket has not been changed. Please retry."));
            }

            if (productIds.Any(id => !existingIds.Contains(id)))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    "One or more products no longer exist in the catalog. Refresh your basket and try again."));
            }
        }

        var customerBasket = MapToCustomerBasket(userId, request);
        var response = await repository.UpdateBasketAsync(customerBasket);
        if (response is null)
        {
            ThrowBasketDoesNotExist(userId);
        }

        return MapToCustomerBasketResponse(response);
    }

    public override async Task<DeleteBasketResponse> DeleteBasket(DeleteBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        await repository.DeleteBasketAsync(userId);
        return new();
    }

    [DoesNotReturn]
    private static void ThrowNotAuthenticated() => throw new RpcException(new Status(StatusCode.Unauthenticated, "The caller is not authenticated."));

    [DoesNotReturn]
    private static void ThrowBasketDoesNotExist(string userId) => throw new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {userId} does not exist"));

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket)
    {
        var response = new CustomerBasketResponse();

        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItem()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }

    private static CustomerBasket MapToCustomerBasket(string userId, UpdateBasketRequest customerBasketRequest)
    {
        var response = new CustomerBasket
        {
            BuyerId = userId
        };

        foreach (var item in customerBasketRequest.Items)
        {
            response.Items.Add(new()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }
}
