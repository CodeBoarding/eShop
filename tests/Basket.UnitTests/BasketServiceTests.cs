using System.Security.Claims;
using System.Net.Http;
using System.Linq;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Grpc;
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using eShop.Basket.API.Model;
using eShop.Basket.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Grpc.Core;
using BasketItem = eShop.Basket.API.Model.BasketItem;

namespace eShop.Basket.UnitTests;

[TestClass]
public class BasketServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task GetBasketReturnsEmptyForNoUser()
    {
        var mockRepository = Substitute.For<IBasketRepository>();
        var service = CreateService(mockRepository);
        var serverCallContext = TestServerCallContext.Create(cancellationToken: TestContext.CancellationToken);
        serverCallContext.SetUserState("__HttpContext", new DefaultHttpContext());

        var response = await service.GetBasket(new GetBasketRequest(), serverCallContext);

        Assert.IsInstanceOfType<CustomerBasketResponse>(response);
        Assert.IsEmpty(response.Items);
    }

    [TestMethod]
    public async Task GetBasketReturnsItemsForValidUserId()
    {
        var mockRepository = Substitute.For<IBasketRepository>();
        List<BasketItem> items = [new BasketItem { Id = "some-id" }];
        mockRepository.GetBasketAsync("1").Returns(Task.FromResult(new CustomerBasket { BuyerId = "1", Items = items }));
        var service = CreateService(mockRepository);
        var serverCallContext = TestServerCallContext.Create(cancellationToken: TestContext.CancellationToken);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "1")]));
        serverCallContext.SetUserState("__HttpContext", httpContext);

        var response = await service.GetBasket(new GetBasketRequest(), serverCallContext);

        Assert.IsInstanceOfType<CustomerBasketResponse>(response);
        Assert.HasCount(1, response.Items);
    }

    [TestMethod]
    public async Task GetBasketReturnsEmptyForInvalidUserId()
    {
        var mockRepository = Substitute.For<IBasketRepository>();
        List<BasketItem> items = [new BasketItem { Id = "some-id" }];
        mockRepository.GetBasketAsync("1").Returns(Task.FromResult(new CustomerBasket { BuyerId = "1", Items = items }));
        var service = CreateService(mockRepository);
        var serverCallContext = TestServerCallContext.Create(cancellationToken: TestContext.CancellationToken);
        var httpContext = new DefaultHttpContext();
        serverCallContext.SetUserState("__HttpContext", httpContext);

        var response = await service.GetBasket(new GetBasketRequest(), serverCallContext);

        Assert.IsInstanceOfType<CustomerBasketResponse>(response);
        Assert.IsEmpty(response.Items);
    }

    [TestMethod]
    public async Task UpdateBasketPersistsItemsForAuthenticatedUser()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(call => call.Arg<CustomerBasket>());
        var service = CreateService(repository);
        var context = CreateContext("buyer-1");
        var request = new UpdateBasketRequest();
        request.Items.Add(new eShop.Basket.API.Grpc.BasketItem { ProductId = 42, Quantity = 3 });

        var response = await service.UpdateBasket(request, context);

        Assert.HasCount(1, response.Items);
        Assert.AreEqual(42, response.Items[0].ProductId);
        Assert.AreEqual(3, response.Items[0].Quantity);
        await repository.Received(1).UpdateBasketAsync(Arg.Is<CustomerBasket>(basket =>
            basket.BuyerId == "buyer-1" &&
            basket.Items.Count == 1 &&
            basket.Items[0].ProductId == 42 &&
            basket.Items[0].Quantity == 3));
    }

    [TestMethod]
    public async Task UpdateBasketRejectsAnonymousUser()
    {
        var repository = Substitute.For<IBasketRepository>();
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UpdateBasket(new UpdateBasketRequest(), CreateContext(null!)));

        Assert.AreEqual(StatusCode.Unauthenticated, exception.StatusCode);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task UpdateBasketReturnsNotFoundWhenRepositoryCannotPersist()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(Task.FromResult<CustomerBasket>(null!));
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UpdateBasket(new UpdateBasketRequest(), CreateContext("missing")));

        Assert.AreEqual(StatusCode.NotFound, exception.StatusCode);
    }

    [TestMethod]
    public async Task DeleteBasketRemovesAuthenticatedUsersBasket()
    {
        var repository = Substitute.For<IBasketRepository>();
        var service = CreateService(repository);

        await service.DeleteBasket(new DeleteBasketRequest(), CreateContext("buyer-1"));

        await repository.Received(1).DeleteBasketAsync("buyer-1");
    }

    [TestMethod]
    public async Task OrderStartedEventRemovesUsersBasket()
    {
        var repository = Substitute.For<IBasketRepository>();
        var handler = new OrderStartedIntegrationEventHandler(
            repository,
            NullLogger<OrderStartedIntegrationEventHandler>.Instance);

        await handler.Handle(new OrderStartedIntegrationEvent("buyer-1"));

        await repository.Received(1).DeleteBasketAsync("buyer-1");
    }

    [TestMethod]
    [DataRow(System.Net.HttpStatusCode.NotFound, StatusCode.InvalidArgument)]
    [DataRow(System.Net.HttpStatusCode.ServiceUnavailable, StatusCode.Unavailable)]
    public async Task CatalogFailureDoesNotOverwriteBasket(System.Net.HttpStatusCode status, StatusCode expected)
    {
        var repository = Substitute.For<IBasketRepository>();
        var paths = new List<string>();
        var service = CreateService(repository, request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(paths.Count == 1 ? System.Net.HttpStatusCode.OK : status);
        });
        var request = new UpdateBasketRequest();
        request.Items.Add(new eShop.Basket.API.Grpc.BasketItem { ProductId = 42, Quantity = 3 });
        request.Items.Add(new eShop.Basket.API.Grpc.BasketItem { ProductId = 99, Quantity = 1 });

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.UpdateBasket(request, CreateContext("buyer-1")));

        Assert.AreEqual(expected, exception.StatusCode);
        CollectionAssert.AreEqual(new[] { "/api/catalog/items/42?api-version=2.0", "/api/catalog/items/99?api-version=2.0" }, paths);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task DuplicateProductsAreValidatedOnceAndQuantitiesPreserved()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>()).Returns(call => call.Arg<CustomerBasket>());
        var calls = 0;
        var service = CreateService(repository, _ => { calls++; return new HttpResponseMessage(System.Net.HttpStatusCode.OK); });
        var request = new UpdateBasketRequest();
        request.Items.Add(new eShop.Basket.API.Grpc.BasketItem { ProductId = 42, Quantity = 3 });
        request.Items.Add(new eShop.Basket.API.Grpc.BasketItem { ProductId = 42, Quantity = 7 });

        var response = await service.UpdateBasket(request, CreateContext("buyer-1"));

        Assert.AreEqual(1, calls);
        CollectionAssert.AreEqual(new[] { 3, 7 }, response.Items.Select(item => item.Quantity).ToArray());
    }

    [TestMethod]
    public async Task EmptyBasketDoesNotRequireCatalog()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>()).Returns(call => call.Arg<CustomerBasket>());
        var service = CreateService(repository, _ => throw new AssertFailedException("Catalog must not be called."));

        var response = await service.UpdateBasket(new UpdateBasketRequest(), CreateContext("buyer-1"));

        Assert.IsEmpty(response.Items);
        await repository.Received(1).UpdateBasketAsync(Arg.Is<CustomerBasket>(basket => basket.Items.Count == 0));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task InvalidProductIdsAreRejectedWithoutCallingCatalog(int productId)
    {
        var repository = Substitute.For<IBasketRepository>();
        var service = CreateService(repository, _ => throw new AssertFailedException("Catalog must not be called."));
        var request = new UpdateBasketRequest();
        request.Items.Add(new eShop.Basket.API.Grpc.BasketItem { ProductId = productId, Quantity = 1 });

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.UpdateBasket(request, CreateContext("buyer-1")));

        Assert.AreEqual(StatusCode.InvalidArgument, exception.StatusCode);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    private static BasketService CreateService(IBasketRepository repository, Func<HttpRequestMessage, HttpResponseMessage> respond = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("catalog").Returns(_ => new HttpClient(new CatalogHandler(respond)) { BaseAddress = new Uri("http://catalog-api") });
        return new BasketService(repository, NullLogger<BasketService>.Instance, factory);
    }

    private sealed class CatalogHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private TestServerCallContext CreateContext(string userId)
    {
        var context = TestServerCallContext.Create(cancellationToken: TestContext.CancellationToken);
        var httpContext = new DefaultHttpContext();
        if (userId is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)]));
        }
        context.SetUserState("__HttpContext", httpContext);
        return context;
    }
}
