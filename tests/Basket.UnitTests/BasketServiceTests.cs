using System.Security.Claims;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Grpc;
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using eShop.Basket.API.Model;
using eShop.Basket.API.Services;
using System.Linq;
using System.Net;
using System.Net.Http;
using Polly.CircuitBreaker;
using Polly.Timeout;
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
        using var handler = new CatalogHandler("[]");
        var service = CreateService(repository, handler);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UpdateBasket(Request((42, 1)), CreateContext(null!)));

        Assert.AreEqual(StatusCode.Unauthenticated, exception.StatusCode);
        Assert.AreEqual(0, handler.Calls);
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
    public async Task UpdateBasketChecksEntireBatchBeforePersisting()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>()).Returns(call => call.Arg<CustomerBasket>());
        using var handler = new CatalogHandler("[{\"id\":7},{\"id\":42}]");
        var service = CreateService(repository, handler);
        var request = Request((42, 3), (7, 2));

        var response = await service.UpdateBasket(request, CreateContext("buyer-1"));

        Assert.AreEqual("/api/catalog/items/by?api-version=2.0&ids=42&ids=7", handler.RequestUri?.PathAndQuery);
        Assert.AreEqual(1, handler.Calls);
        CollectionAssert.AreEqual(new[] { 42, 7 }, response.Items.Select(item => item.ProductId).ToArray());
        CollectionAssert.AreEqual(new[] { 3, 2 }, response.Items.Select(item => item.Quantity).ToArray());
        await repository.Received(1).UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("[{\"id\":42}]")]
    [DataRow("[{\"id\":42},{\"id\":99}]")]
    public async Task UpdateBasketRejectsMissingProductsWithoutChangingStoredBasket(string json)
    {
        var repository = Substitute.For<IBasketRepository>();
        using var handler = new CatalogHandler(json);
        var service = CreateService(repository, handler);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UpdateBasket(Request((42, 3), (7, 2)), CreateContext("buyer-1")));

        Assert.AreEqual(StatusCode.FailedPrecondition, exception.StatusCode);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    [DataRow("[]", 503)]
    [DataRow("invalid-json", 200)]
    [DataRow("null", 200)]
    public async Task CatalogFailureLeavesBasketUnchanged(string json, int status)
    {
        var repository = Substitute.For<IBasketRepository>();
        using var handler = new CatalogHandler(json, (HttpStatusCode)status);
        var service = CreateService(repository, handler);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UpdateBasket(Request((42, 1)), CreateContext("buyer-1")));

        Assert.AreEqual(StatusCode.Unavailable, exception.StatusCode);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    [DataRow("network")]
    [DataRow("http-timeout")]
    [DataRow("resilience-timeout")]
    [DataRow("circuit-open")]
    public async Task CatalogTransportFailuresAreRetryable(string failure)
    {
        var repository = Substitute.For<IBasketRepository>();
        using var handler = new CatalogHandler("[]")
        {
            Failure = failure switch
            {
                "network" => new HttpRequestException(),
                "http-timeout" => new TaskCanceledException(),
                "resilience-timeout" => new TimeoutRejectedException(),
                _ => new BrokenCircuitException()
            }
        };

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            CreateService(repository, handler).UpdateBasket(Request((42, 1)), CreateContext("buyer-1")));

        Assert.AreEqual(StatusCode.Unavailable, exception.StatusCode);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task CallerCancellationDoesNotPersistOrBecomeCatalogUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        var context = TestServerCallContext.Create(cancellationToken: cancellation.Token);
        context.SetUserState("__HttpContext", new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "buyer-1")]))
        });
        var repository = Substitute.For<IBasketRepository>();
        using var handler = new CatalogHandler("[]") { Cancel = cancellation.Cancel };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService(repository, handler).UpdateBasket(Request((42, 1)), context));

        Assert.IsTrue(cancellation.IsCancellationRequested);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(-1, 1)]
    [DataRow(42, 0)]
    [DataRow(42, -1)]
    public async Task InvalidLinesDoNotCallCatalogOrPersist(int productId, int quantity)
    {
        await AssertRejectedBeforeLookup(Request((productId, quantity)));
    }

    [TestMethod]
    public async Task DuplicateProductsDoNotCallCatalogOrPersist()
    {
        await AssertRejectedBeforeLookup(Request((42, 1), (42, 2)));
    }

    [TestMethod]
    [DataRow(100, true)]
    [DataRow(101, false)]
    public async Task BasketSizeLimitBoundsCatalogRequest(int count, bool accepted)
    {
        var request = Request(Enumerable.Range(1, count).Select(id => (id, 1)).ToArray());
        if (!accepted)
        {
            await AssertRejectedBeforeLookup(request);
            return;
        }

        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>()).Returns(call => call.Arg<CustomerBasket>());
        using var handler = new CatalogHandler("[" + string.Join(",", Enumerable.Range(1, count).Select(id => $"{{\"id\":{id}}}")) + "]");
        var response = await CreateService(repository, handler).UpdateBasket(request, CreateContext("buyer-1"));

        Assert.HasCount(100, response.Items);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task EmptyBasketCanBeSavedWithoutCatalog()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>()).Returns(call => call.Arg<CustomerBasket>());
        using var handler = new CatalogHandler("[]", HttpStatusCode.ServiceUnavailable);

        var response = await CreateService(repository, handler).UpdateBasket(Request(), CreateContext("buyer-1"));

        Assert.IsEmpty(response.Items);
        Assert.AreEqual(0, handler.Calls);
        await repository.Received(1).UpdateBasketAsync(Arg.Is<CustomerBasket>(basket => basket.Items.Count == 0));
    }

    private async Task AssertRejectedBeforeLookup(UpdateBasketRequest request)
    {
        var repository = Substitute.For<IBasketRepository>();
        using var handler = new CatalogHandler("[]");
        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            CreateService(repository, handler).UpdateBasket(request, CreateContext("buyer-1")));

        Assert.AreEqual(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.AreEqual(0, handler.Calls);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    private static UpdateBasketRequest Request(params (int Id, int Quantity)[] items)
    {
        var request = new UpdateBasketRequest();
        request.Items.AddRange(items.Select(item => new eShop.Basket.API.Grpc.BasketItem
        {
            ProductId = item.Id,
            Quantity = item.Quantity
        }));
        return request;
    }

    private static BasketService CreateService(IBasketRepository repository, CatalogHandler handler = null) =>
        new(repository, NullLogger<BasketService>.Instance,
            new CatalogClient(new HttpClient(handler ?? new CatalogHandler("[{\"id\":42}]"))
            {
                BaseAddress = new Uri("http://catalog-api")
            }));

    private sealed class CatalogHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri RequestUri { get; private set; }
        public int Calls { get; private set; }
        public Exception Failure { get; init; }
        public Action Cancel { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            Cancel?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<HttpResponseMessage>(Failure);
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
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
