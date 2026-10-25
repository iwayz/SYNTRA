using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Syntra.Tests;

// Stock accuracy under concurrent requests. Each round uses its own product and
// warehouse so rounds can't interfere, and fires the competing requests at the
// same moment against the real pipeline and a real SQL Server.
public class StockConcurrencyTests : IClassFixture<TenantIsolationFixture>
{
    private readonly TenantIsolationFixture _fixture;

    public StockConcurrencyTests(TenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var request = _fixture.CreateRequest(method, _fixture.TenantASlug, path, _fixture.TenantAToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _fixture.Client.SendAsync(request);
    }

    private async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    // A product with `stock` units on hand in a fresh warehouse in the Main branch.
    private async Task<(int ProductId, int UnitId, int WarehouseId, int CustomerId)> StockedProductAsync(decimal stock)
    {
        var unit = await CreatedAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("u"), name = "Piece" }));
        var unitId = unit.GetProperty("unitOfMeasureId").GetInt32();
        var product = await CreatedAsync(await SendAsync(HttpMethod.Post, "/inventory/products",
            new { sku = Unique("sku"), name = "Widget", baseUnitOfMeasureId = unitId }));
        var productId = product.GetProperty("productId").GetInt32();
        var warehouse = await CreatedAsync(await SendAsync(HttpMethod.Post, "/inventory/warehouses", new { name = "WH", branchId = 1 }));
        var warehouseId = warehouse.GetProperty("warehouseId").GetInt32();
        var customer = await CreatedAsync(await SendAsync(HttpMethod.Post, "/sales/customers", new { name = "Cust" }));
        var customerId = customer.GetProperty("customerId").GetInt32();

        await CreatedAsync(await SendAsync(HttpMethod.Post, "/inventory/stock-adjustments",
            new { productId, warehouseId, quantity = stock, notes = "seed" }));

        return (productId, unitId, warehouseId, customerId);
    }

    private async Task<int> DraftSalesOrderAsync(int customerId, int warehouseId, int productId, int unitId, decimal quantity)
    {
        var order = await CreatedAsync(await SendAsync(HttpMethod.Post, "/sales/sales-orders", new
        {
            customerId,
            warehouseId,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId, unitOfMeasureId = unitId, quantity, unitPrice = 5 } }
        }));
        return order.GetProperty("salesOrderId").GetInt32();
    }

    private async Task<decimal> OnHandAsync(int productId, int warehouseId)
    {
        var balances = await (await SendAsync(HttpMethod.Get, $"/inventory/stock-balance?productId={productId}&warehouseId={warehouseId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        return balances.EnumerateArray().Sum(b => b.GetProperty("quantityOnHand").GetDecimal());
    }

    [Fact]
    public async Task CompetingSalesOrderConfirms_NeverOversell()
    {
        // 10 on hand, 8 orders of 3 each: at most 3 can be fulfilled (9), never 4 (12).
        for (var round = 0; round < 5; round++)
        {
            var (productId, unitId, warehouseId, customerId) = await StockedProductAsync(10);

            var orderIds = new List<int>();
            for (var i = 0; i < 8; i++)
            {
                orderIds.Add(await DraftSalesOrderAsync(customerId, warehouseId, productId, unitId, 3));
            }

            var statuses = await Task.WhenAll(orderIds.Select(async id =>
                (await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{id}/confirm")).StatusCode));

            var succeeded = statuses.Count(s => s == HttpStatusCode.OK);
            var onHand = await OnHandAsync(productId, warehouseId);

            Assert.True(onHand >= 0, $"Round {round}: on hand went negative ({onHand}) after {succeeded} confirmations.");
            Assert.Equal(3, succeeded);
            Assert.Equal(10 - 3 * succeeded, onHand);
        }
    }

    [Fact]
    public async Task SameSalesOrderConfirmedConcurrently_DeductsStockOnce()
    {
        for (var round = 0; round < 5; round++)
        {
            var (productId, unitId, warehouseId, customerId) = await StockedProductAsync(10);
            var orderId = await DraftSalesOrderAsync(customerId, warehouseId, productId, unitId, 3);

            var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
                (await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{orderId}/confirm")).StatusCode));

            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
            Assert.Equal(7, await OnHandAsync(productId, warehouseId));
        }
    }
}
