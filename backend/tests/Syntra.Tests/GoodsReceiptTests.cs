using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Syntra.Tests;

public class GoodsReceiptTests : IClassFixture<TenantIsolationFixture>
{
    private readonly TenantIsolationFixture _fixture;

    public GoodsReceiptTests(TenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? token = null)
    {
        var request = _fixture.CreateRequest(method, _fixture.TenantASlug, path, token ?? _fixture.TenantAToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _fixture.Client.SendAsync(request);
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private record Setup(int ProductId, int UnitId, int WarehouseId, int SupplierId);

    private async Task<Setup> NewSetupAsync()
    {
        var unit = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("u"), name = "Piece" }), HttpStatusCode.Created);
        var unitId = unit.GetProperty("unitOfMeasureId").GetInt32();
        var product = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/products",
            new { sku = Unique("sku"), name = "Widget", baseUnitOfMeasureId = unitId }), HttpStatusCode.Created);
        var warehouse = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/warehouses", new { name = "WH", branchId = 1 }), HttpStatusCode.Created);
        var supplier = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/suppliers", new { name = "Supp" }), HttpStatusCode.Created);

        return new Setup(
            product.GetProperty("productId").GetInt32(),
            unitId,
            warehouse.GetProperty("warehouseId").GetInt32(),
            supplier.GetProperty("supplierId").GetInt32());
    }

    // A confirmed PO with one line of `quantity` (in `unitId`, default the base unit).
    private async Task<(int OrderId, int LineId)> ConfirmedPoAsync(Setup s, decimal quantity, int? unitId = null)
    {
        var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", new
        {
            supplierId = s.SupplierId,
            warehouseId = s.WarehouseId,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId = s.ProductId, unitOfMeasureId = unitId ?? s.UnitId, quantity, unitPrice = 5 } }
        }), HttpStatusCode.Created);

        var orderId = order.GetProperty("purchaseOrderId").GetInt32();
        var lineId = order.GetProperty("lines")[0].GetProperty("purchaseOrderLineId").GetInt32();

        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/confirm"), HttpStatusCode.OK);
        return (orderId, lineId);
    }

    private Task<HttpResponseMessage> ReceiveAsync(int orderId, int lineId, decimal quantity, string? notes = null) =>
        SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/goods-receipts",
            new { notes = (string?)null, lines = new[] { new { purchaseOrderLineId = lineId, quantity, notes } } });

    private async Task<decimal> OnHandAsync(Setup s)
    {
        var balances = await JsonAsync(await SendAsync(HttpMethod.Get,
            $"/inventory/stock-balance?productId={s.ProductId}&warehouseId={s.WarehouseId}"), HttpStatusCode.OK);
        return balances.EnumerateArray().Sum(b => b.GetProperty("quantityOnHand").GetDecimal());
    }

    private async Task<JsonElement> GetOrderAsync(int orderId) =>
        await JsonAsync(await SendAsync(HttpMethod.Get, $"/purchasing/purchase-orders/{orderId}"), HttpStatusCode.OK);

    [Fact]
    public async Task PartialReceipts_AccumulateUntilTheOrderIsFullyReceived()
    {
        var s = await NewSetupAsync();
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);

        var first = await JsonAsync(await ReceiveAsync(orderId, lineId, 4, "2 cartons damaged"), HttpStatusCode.Created);
        Assert.Equal("2 cartons damaged", first.GetProperty("lines")[0].GetProperty("notes").GetString());

        var afterFirst = await GetOrderAsync(orderId);
        Assert.Equal("PartiallyReceived", afterFirst.GetProperty("status").GetString());
        Assert.Equal(4, afterFirst.GetProperty("lines")[0].GetProperty("quantityReceived").GetDecimal());
        Assert.Equal(4, await OnHandAsync(s));

        await JsonAsync(await ReceiveAsync(orderId, lineId, 6), HttpStatusCode.Created);

        var afterSecond = await GetOrderAsync(orderId);
        Assert.Equal("Received", afterSecond.GetProperty("status").GetString());
        Assert.Equal(2, afterSecond.GetProperty("goodsReceipts").GetArrayLength());
        Assert.Equal(10, await OnHandAsync(s));
    }

    [Fact]
    public async Task ReceivingMoreThanOutstanding_IsRejectedAndChangesNothing()
    {
        var s = await NewSetupAsync();
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);
        await JsonAsync(await ReceiveAsync(orderId, lineId, 7), HttpStatusCode.Created);

        var over = await ReceiveAsync(orderId, lineId, 4);

        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Equal(7, await OnHandAsync(s));
        Assert.Equal(7, (await GetOrderAsync(orderId)).GetProperty("lines")[0].GetProperty("quantityReceived").GetDecimal());
    }

    [Fact]
    public async Task ReceiveAll_TakesOnlyWhatIsStillOutstanding()
    {
        var s = await NewSetupAsync();
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);
        await JsonAsync(await ReceiveAsync(orderId, lineId, 3), HttpStatusCode.Created);

        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/receive"), HttpStatusCode.OK);

        Assert.Equal(10, await OnHandAsync(s));
        Assert.Equal("Received", (await GetOrderAsync(orderId)).GetProperty("status").GetString());

        // Nothing left to receive now.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/receive")).StatusCode);
    }

    [Fact]
    public async Task ClosingAPartiallyReceivedOrder_KeepsTheShortfallVisible()
    {
        var s = await NewSetupAsync();
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);

        // Only a partially received order can be closed.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/close")).StatusCode);

        await JsonAsync(await ReceiveAsync(orderId, lineId, 6), HttpStatusCode.Created);
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/close"), HttpStatusCode.OK);

        var order = await GetOrderAsync(orderId);
        Assert.Equal("Received", order.GetProperty("status").GetString());
        var line = order.GetProperty("lines")[0];
        Assert.Equal(10, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(6, line.GetProperty("quantityReceived").GetDecimal());
        Assert.Equal(6, await OnHandAsync(s));

        // A closed order takes no more goods.
        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(orderId, lineId, 1)).StatusCode);
    }

    [Fact]
    public async Task PartiallyReceivedOrder_CannotBeCancelled()
    {
        var s = await NewSetupAsync();
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);
        await JsonAsync(await ReceiveAsync(orderId, lineId, 2), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/cancel")).StatusCode);
    }

    [Fact]
    public async Task DraftOrder_CannotReceiveGoods()
    {
        var s = await NewSetupAsync();
        var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", new
        {
            supplierId = s.SupplierId,
            warehouseId = s.WarehouseId,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity = 5, unitPrice = 5 } }
        }), HttpStatusCode.Created);
        var orderId = order.GetProperty("purchaseOrderId").GetInt32();
        var lineId = order.GetProperty("lines")[0].GetProperty("purchaseOrderLineId").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(orderId, lineId, 1)).StatusCode);
    }

    [Fact]
    public async Task ReceiptLines_MustBelongToTheOrderAndNotRepeat()
    {
        var s = await NewSetupAsync();
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);
        var (_, otherLineId) = await ConfirmedPoAsync(s, 10);

        var foreign = await ReceiveAsync(orderId, otherLineId, 1);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var duplicate = await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/goods-receipts", new
        {
            lines = new[]
            {
                new { purchaseOrderLineId = lineId, quantity = 1m, notes = (string?)null },
                new { purchaseOrderLineId = lineId, quantity = 1m, notes = (string?)null }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        Assert.Equal(0, await OnHandAsync(s));
    }

    [Fact]
    public async Task ReceivingInAnotherUnit_ConvertsToTheBaseUnit()
    {
        var s = await NewSetupAsync();
        var box = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("box"), name = "Box" }), HttpStatusCode.Created);
        var boxId = box.GetProperty("unitOfMeasureId").GetInt32();
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/inventory/products/{s.ProductId}/unit-conversions",
            new { unitOfMeasureId = boxId, conversionFactor = 12 }), HttpStatusCode.Created);

        var (orderId, lineId) = await ConfirmedPoAsync(s, 5, unitId: boxId);
        await JsonAsync(await ReceiveAsync(orderId, lineId, 2), HttpStatusCode.Created);

        Assert.Equal(24, await OnHandAsync(s));
        Assert.Equal("PartiallyReceived", (await GetOrderAsync(orderId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ConcurrentReceipts_CannotExceedTheOrderedQuantity()
    {
        // 10 ordered, six simultaneous receipts of 4: only two fit (8); a third
        // would make 12. Without the stock lock they could all read 0 received.
        for (var round = 0; round < 3; round++)
        {
            var s = await NewSetupAsync();
            var (orderId, lineId) = await ConfirmedPoAsync(s, 10);

            var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
                (await ReceiveAsync(orderId, lineId, 4)).StatusCode));

            Assert.Equal(2, statuses.Count(x => x == HttpStatusCode.Created));
            Assert.Equal(8, await OnHandAsync(s));
            Assert.Equal(8, (await GetOrderAsync(orderId)).GetProperty("lines")[0].GetProperty("quantityReceived").GetDecimal());
        }
    }

    [Fact]
    public async Task ManualAdjustment_CannotTakeStockBelowZero()
    {
        var s = await NewSetupAsync();
        object Adjust(decimal quantity) => new { productId = s.ProductId, warehouseId = s.WarehouseId, quantity, notes = "test" };

        await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", Adjust(5)), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", Adjust(-6))).StatusCode);
        Assert.Equal(5, await OnHandAsync(s));

        await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", Adjust(-5)), HttpStatusCode.Created);
        Assert.Equal(0, await OnHandAsync(s));
    }
}
