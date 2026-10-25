using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Syntra.Tests;

public class ReturnTests : IClassFixture<TenantIsolationFixture>
{
    private const string Password = "P@ssw0rd123!";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TenantIsolationFixture _fixture;

    public ReturnTests(TenantIsolationFixture fixture)
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private record Scenario(
        int ProductId,
        int UnitId,
        int BranchId,
        int Warehouse,
        int CustomerId,
        int SupplierId,
        string StaffToken,
        string StrangerToken);

    private async Task<string> StaffTokenAsync(int branchId)
    {
        var email = $"staff-{Guid.NewGuid():N}@isolation-test.local";
        await JsonAsync(await SendAsync(HttpMethod.Post, "/tenancy/users",
            new { email, password = Password, isAdmin = false, branchIds = new[] { branchId } }), HttpStatusCode.Created);

        var login = _fixture.CreateRequest(HttpMethod.Post, _fixture.TenantASlug, "/auth/login");
        login.Content = JsonContent.Create(new { email, password = Password });
        return (await JsonAsync(await _fixture.Client.SendAsync(login), HttpStatusCode.OK)).GetProperty("token").GetString()!;
    }

    private async Task<(int BranchId, int WarehouseId)> BranchWithWarehouseAsync(string label)
    {
        var branch = await JsonAsync(await SendAsync(HttpMethod.Post, "/tenancy/branches", new { code = Unique(label), name = $"Branch {label}" }), HttpStatusCode.Created);
        var branchId = branch.GetProperty("branchId").GetInt32();
        var warehouse = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/warehouses", new { name = $"WH {label}", branchId }), HttpStatusCode.Created);
        return (branchId, warehouse.GetProperty("warehouseId").GetInt32());
    }

    private async Task<Scenario> NewScenarioAsync(string mode = "None")
    {
        var unit = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("u"), name = "Piece" }), HttpStatusCode.Created);
        var unitId = unit.GetProperty("unitOfMeasureId").GetInt32();
        var product = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/products",
            new { sku = Unique("sku"), name = "Widget", baseUnitOfMeasureId = unitId, batchTracking = mode }), HttpStatusCode.Created);
        var customer = await JsonAsync(await SendAsync(HttpMethod.Post, "/sales/customers", new { name = "Cust" }), HttpStatusCode.Created);
        var supplier = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/suppliers", new { name = "Supp" }), HttpStatusCode.Created);

        var (branchId, warehouse) = await BranchWithWarehouseAsync("R");
        var (otherBranch, _) = await BranchWithWarehouseAsync("N");

        return new Scenario(
            product.GetProperty("productId").GetInt32(), unitId, branchId, warehouse,
            customer.GetProperty("customerId").GetInt32(), supplier.GetProperty("supplierId").GetInt32(),
            await StaffTokenAsync(branchId), await StaffTokenAsync(otherBranch));
    }

    private Task<HttpResponseMessage> AdjustAsync(Scenario s, decimal quantity, string? batch = null, DateOnly? expiry = null) =>
        SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", new
        {
            productId = s.ProductId,
            warehouseId = s.Warehouse,
            quantity,
            notes = "seed",
            batchNumber = batch,
            expiryDate = expiry
        });

    private async Task SeedAsync(Scenario s, decimal quantity, string? batch = null, DateOnly? expiry = null) =>
        await JsonAsync(await AdjustAsync(s, quantity, batch, expiry), HttpStatusCode.Created);

    private async Task<decimal> OnHandAsync(Scenario s, string? batch = null)
    {
        var balances = await JsonAsync(await SendAsync(HttpMethod.Get,
            $"/inventory/stock-balance?productId={s.ProductId}&warehouseId={s.Warehouse}"), HttpStatusCode.OK);

        return balances.EnumerateArray()
            .Where(b => batch is null || b.GetProperty("batchNumber").GetString() == batch)
            .Sum(b => b.GetProperty("quantityOnHand").GetDecimal());
    }

    private async Task<string> ErrorTextAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.BadRequest)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- sales helpers ----

    private async Task<(int OrderId, int LineId)> ConfirmedSaleAsync(Scenario s, decimal quantity, int? unitId = null, bool confirm = true)
    {
        var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/sales/sales-orders", new
        {
            customerId = s.CustomerId,
            warehouseId = s.Warehouse,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId = s.ProductId, unitOfMeasureId = unitId ?? s.UnitId, quantity, unitPrice = 5 } }
        }), HttpStatusCode.Created);

        var orderId = order.GetProperty("salesOrderId").GetInt32();
        var lineId = order.GetProperty("lines")[0].GetProperty("salesOrderLineId").GetInt32();

        if (confirm)
        {
            await JsonAsync(await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{orderId}/confirm"), HttpStatusCode.OK);
        }

        return (orderId, lineId);
    }

    private record SaleLine(int LineId, decimal Quantity, string? Batch = null, string Disposition = "Restock");

    private Task<HttpResponseMessage> ReturnSaleAsync(int orderId, string? token, params SaleLine[] lines) =>
        SendAsync(HttpMethod.Post, $"/sales/sales-orders/{orderId}/returns", new
        {
            reason = "Customer changed their mind",
            lines = lines.Select(l => new
            {
                salesOrderLineId = l.LineId,
                quantity = l.Quantity,
                batchNumber = l.Batch,
                disposition = l.Disposition
            }).ToArray()
        }, token);

    private async Task<JsonElement> SaleOrderAsync(int orderId) =>
        await JsonAsync(await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{orderId}"), HttpStatusCode.OK);

    private static decimal ReturnedOn(JsonElement order) =>
        order.GetProperty("lines")[0].GetProperty("quantityReturned").GetDecimal();

    // ---- sales returns ----

    [Fact]
    public async Task SalesReturn_RestocksTheGoodsAndAccumulatesAgainstTheLine()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var (orderId, lineId) = await ConfirmedSaleAsync(s, 6);
        Assert.Equal(4, await OnHandAsync(s));

        var first = await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 2)), HttpStatusCode.Created);
        Assert.Equal(6, await OnHandAsync(s));
        Assert.Equal(2, ReturnedOn(await SaleOrderAsync(orderId)));

        // Movement is a SalesReturn tied to the return document.
        var returnId = first.GetProperty("salesReturnId").GetInt32();
        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?referenceId={returnId}&productId={s.ProductId}"), HttpStatusCode.OK);
        var returnMovement = movements.EnumerateArray().Single(m => m.GetProperty("movementType").GetString() == "SalesReturn");
        Assert.Equal(2, returnMovement.GetProperty("quantity").GetDecimal());

        await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 4)), HttpStatusCode.Created);
        Assert.Equal(10, await OnHandAsync(s));
        Assert.Equal(6, ReturnedOn(await SaleOrderAsync(orderId)));

        // The whole line is back: nothing more can be returned.
        Assert.Contains("only 0 can still be returned", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1))));
        Assert.Equal(10, await OnHandAsync(s));

        // The order shows its returns.
        Assert.Equal(2, (await SaleOrderAsync(orderId)).GetProperty("returns").GetArrayLength());
    }

    [Fact]
    public async Task SalesReturn_CannotExceedWhatWasSold()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var (orderId, lineId) = await ConfirmedSaleAsync(s, 3);

        Assert.Contains("only 3 can still be returned", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 4))));
        Assert.Equal(7, await OnHandAsync(s));
        Assert.Equal(0, ReturnedOn(await SaleOrderAsync(orderId)));

        // A line from another order is not accepted either.
        var (_, otherLineId) = await ConfirmedSaleAsync(s, 1);
        Assert.Contains("does not belong", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(otherLineId, 1))));
    }

    [Fact]
    public async Task SalesReturn_WrittenOffGoodsNeverEnterStock_ButStillUseUpTheReturnableQuantity()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var (orderId, lineId) = await ConfirmedSaleAsync(s, 5);

        var written = await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 2, Disposition: "WriteOff")), HttpStatusCode.Created);

        Assert.Equal(5, await OnHandAsync(s));   // unchanged: the damaged goods are not stock
        Assert.Equal(2, ReturnedOn(await SaleOrderAsync(orderId)));

        var returnId = written.GetProperty("salesReturnId").GetInt32();
        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?referenceId={returnId}&productId={s.ProductId}"), HttpStatusCode.OK);
        Assert.DoesNotContain(movements.EnumerateArray(), m => m.GetProperty("movementType").GetString() == "SalesReturn");

        // Mixed return: one line restocked, the rest of the quantity written off.
        await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1), new SaleLine(lineId, 2, Disposition: "WriteOff")), HttpStatusCode.Created);
        Assert.Equal(6, await OnHandAsync(s));
        Assert.Equal(5, ReturnedOn(await SaleOrderAsync(orderId)));
    }

    [Fact]
    public async Task SalesReturn_NeedsAConfirmedOrder()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);

        var (draftId, draftLine) = await ConfirmedSaleAsync(s, 2, confirm: false);
        Assert.Contains("Confirmed", await ErrorTextAsync(await ReturnSaleAsync(draftId, s.StaffToken, new SaleLine(draftLine, 1))));

        var (cancelledId, cancelledLine) = await ConfirmedSaleAsync(s, 2, confirm: false);
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{cancelledId}/cancel"), HttpStatusCode.OK);
        Assert.Contains("Confirmed", await ErrorTextAsync(await ReturnSaleAsync(cancelledId, s.StaffToken, new SaleLine(cancelledLine, 1))));
    }

    [Fact]
    public async Task SalesReturn_ConvertsTheOrderUnitToTheBaseUnit()
    {
        var s = await NewScenarioAsync();
        var box = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("box"), name = "Box" }), HttpStatusCode.Created);
        var boxId = box.GetProperty("unitOfMeasureId").GetInt32();
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/inventory/products/{s.ProductId}/unit-conversions",
            new { unitOfMeasureId = boxId, conversionFactor = 12 }), HttpStatusCode.Created);

        await SeedAsync(s, 48);
        var (orderId, lineId) = await ConfirmedSaleAsync(s, 3, unitId: boxId);   // 36 pieces out
        Assert.Equal(12, await OnHandAsync(s));

        await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1)), HttpStatusCode.Created);   // 1 box back
        Assert.Equal(24, await OnHandAsync(s));
        Assert.Equal(1, ReturnedOn(await SaleOrderAsync(orderId)));
    }

    // ---- sales returns of batch-tracked products ----

    [Fact]
    public async Task SalesReturn_OfATrackedProduct_GoesBackIntoABatchTheOrderSoldFrom()
    {
        var s = await NewScenarioAsync("BatchAndExpiry");
        await SeedAsync(s, 5, "A", Today.AddDays(10));
        await SeedAsync(s, 5, "B", Today.AddDays(40));
        await SeedAsync(s, 5, "C", Today.AddDays(60));
        var (orderId, lineId) = await ConfirmedSaleAsync(s, 8);   // 5 from A, 3 from B

        var returnable = await JsonAsync(await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{orderId}/returnable"), HttpStatusCode.OK);
        var line = returnable.EnumerateArray().Single();
        Assert.True(line.GetProperty("batchTracked").GetBoolean());
        Assert.Equal(8, line.GetProperty("returnable").GetDecimal());
        var batches = line.GetProperty("batches").EnumerateArray().ToDictionary(b => b.GetProperty("batchNumber").GetString()!, b => b.GetProperty("returnable").GetDecimal());
        Assert.Equal(new Dictionary<string, decimal> { ["A"] = 5, ["B"] = 3 }, batches);   // C was never sold from

        Assert.Contains("name the batch", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1))));
        Assert.Contains("does not exist", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1, "NOPE"))));
        Assert.Contains("at most 0", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1, "C"))));
        Assert.Contains("at most 3", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 4, "B"))));

        await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 3, "B"), new SaleLine(lineId, 2, "A")), HttpStatusCode.Created);

        Assert.Equal(3, await OnHandAsync(s, "B") - 2);     // 2 left from the original + 3 returned
        Assert.Equal(2, await OnHandAsync(s, "A"));
        Assert.Equal(5, await OnHandAsync(s, "C"));

        // What is left to return shrinks per batch, and the same batch can't be over-used across requests.
        var after = await JsonAsync(await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{orderId}/returnable"), HttpStatusCode.OK);
        var left = after.EnumerateArray().Single().GetProperty("batches").EnumerateArray()
            .ToDictionary(b => b.GetProperty("batchNumber").GetString()!, b => b.GetProperty("returnable").GetDecimal());
        Assert.Equal(new Dictionary<string, decimal> { ["A"] = 3 }, left);
        // (the line's own limit is hit first here; the per-batch limit is exercised above)
        Assert.Contains("only 1 can still be returned", await ErrorTextAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 2, "A"), new SaleLine(lineId, 2, "A"))));
    }

    // ---- who and where ----

    [Fact]
    public async Task Returns_FollowTheOrdersBranch()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var (orderId, lineId) = await ConfirmedSaleAsync(s, 4);

        // Another branch cannot even see the order.
        Assert.Equal(HttpStatusCode.NotFound, (await ReturnSaleAsync(orderId, s.StrangerToken, new SaleLine(lineId, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{orderId}/returnable", token: s.StrangerToken)).StatusCode);

        // Branch staff can record it themselves - no admin needed.
        await JsonAsync(await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 1)), HttpStatusCode.Created);

        // An inactive branch takes no new returns.
        var branch = await JsonAsync(await SendAsync(HttpMethod.Get, $"/tenancy/branches/{s.BranchId}"), HttpStatusCode.OK);
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/tenancy/branches/{s.BranchId}", new
        {
            code = branch.GetProperty("code").GetString(),
            name = branch.GetProperty("name").GetString(),
            isActive = false
        }), HttpStatusCode.OK);

        Assert.Contains("inactive", await ErrorTextAsync(await ReturnSaleAsync(orderId, _fixture.TenantAToken, new SaleLine(lineId, 1))));
    }

    [Fact]
    public async Task ConcurrentSalesReturns_CannotExceedTheLine()
    {
        for (var round = 0; round < 3; round++)
        {
            var s = await NewScenarioAsync();
            await SeedAsync(s, 20);
            var (orderId, lineId) = await ConfirmedSaleAsync(s, 10);

            var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
                (await ReturnSaleAsync(orderId, s.StaffToken, new SaleLine(lineId, 4))).StatusCode));

            Assert.Equal(2, statuses.Count(x => x == HttpStatusCode.Created));   // 4 + 4 fit in 10, a third would not
            Assert.Equal(8, ReturnedOn(await SaleOrderAsync(orderId)));
            Assert.Equal(18, await OnHandAsync(s));
        }
    }

    // ---- purchase helpers ----

    private record Entry(decimal Quantity, string? Batch = null, DateOnly? Expiry = null);

    // A PO sized to the entries, confirmed and received through a goods receipt with the given batches.
    private async Task<(int OrderId, int LineId)> ReceivedPurchaseAsync(Scenario s, int? unitId = null, params Entry[] entries)
    {
        var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", new
        {
            supplierId = s.SupplierId,
            warehouseId = s.Warehouse,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId = s.ProductId, unitOfMeasureId = unitId ?? s.UnitId, quantity = entries.Sum(e => e.Quantity), unitPrice = 5 } }
        }), HttpStatusCode.Created);

        var orderId = order.GetProperty("purchaseOrderId").GetInt32();
        var lineId = order.GetProperty("lines")[0].GetProperty("purchaseOrderLineId").GetInt32();
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/confirm"), HttpStatusCode.OK);

        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/goods-receipts", new
        {
            lines = entries.Select(e => new
            {
                purchaseOrderLineId = lineId,
                quantity = e.Quantity,
                notes = (string?)null,
                batchNumber = e.Batch,
                expiryDate = e.Expiry
            }).ToArray()
        }), HttpStatusCode.Created);

        return (orderId, lineId);
    }

    private record PurchaseLine(int LineId, decimal Quantity, string? Batch = null);

    private Task<HttpResponseMessage> ReturnPurchaseAsync(int orderId, string? token, params PurchaseLine[] lines) =>
        SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/returns", new
        {
            reason = "Wrong goods delivered",
            lines = lines.Select(l => new { purchaseOrderLineId = l.LineId, quantity = l.Quantity, batchNumber = l.Batch }).ToArray()
        }, token);

    private async Task<JsonElement> PurchaseOrderAsync(int orderId) =>
        await JsonAsync(await SendAsync(HttpMethod.Get, $"/purchasing/purchase-orders/{orderId}"), HttpStatusCode.OK);

    // ---- purchase returns ----

    [Fact]
    public async Task PurchaseReturn_TakesGoodsBackOutOfStock_UpToWhatWasReceived()
    {
        var s = await NewScenarioAsync();
        var (orderId, lineId) = await ReceivedPurchaseAsync(s, null, new Entry(10));
        Assert.Equal(10, await OnHandAsync(s));

        var result = await JsonAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 4)), HttpStatusCode.Created);
        Assert.Equal(6, await OnHandAsync(s));
        Assert.Equal(4, (await PurchaseOrderAsync(orderId)).GetProperty("lines")[0].GetProperty("quantityReturned").GetDecimal());

        var returnId = result.GetProperty("purchaseReturnId").GetInt32();
        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?referenceId={returnId}&productId={s.ProductId}"), HttpStatusCode.OK);
        var movement = movements.EnumerateArray().Single(m => m.GetProperty("movementType").GetString() == "PurchaseReturn");
        Assert.Equal(-4, movement.GetProperty("quantity").GetDecimal());

        Assert.Contains("only 6 can still be returned", await ErrorTextAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 7))));
        await JsonAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 6)), HttpStatusCode.Created);
        Assert.Equal(0, await OnHandAsync(s));
    }

    [Fact]
    public async Task PurchaseReturn_CannotSendBackStockThatWasAlreadySold()
    {
        var s = await NewScenarioAsync();
        var (orderId, lineId) = await ReceivedPurchaseAsync(s, null, new Entry(10));
        await ConfirmedSaleAsync(s, 8);   // 8 sold, 2 left on the shelf

        var error = await ErrorTextAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 5)));
        Assert.Contains("only 2 is still in stock", error);
        Assert.Equal(2, await OnHandAsync(s));

        // What is still there can go back.
        await JsonAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 2)), HttpStatusCode.Created);
        Assert.Equal(0, await OnHandAsync(s));
    }

    [Fact]
    public async Task PurchaseReturn_NeedsReceivedGoods_AndHonoursPartialReceipts()
    {
        var s = await NewScenarioAsync();

        // Not received at all.
        var draft = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", new
        {
            supplierId = s.SupplierId,
            warehouseId = s.Warehouse,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity = 10, unitPrice = 5 } }
        }), HttpStatusCode.Created);
        var draftId = draft.GetProperty("purchaseOrderId").GetInt32();
        var draftLine = draft.GetProperty("lines")[0].GetProperty("purchaseOrderLineId").GetInt32();
        Assert.Contains("received goods", await ErrorTextAsync(await ReturnPurchaseAsync(draftId, s.StaffToken, new PurchaseLine(draftLine, 1))));

        // Partly received: only the received part can be returned.
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{draftId}/confirm"), HttpStatusCode.OK);
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{draftId}/goods-receipts", new
        {
            lines = new[] { new { purchaseOrderLineId = draftLine, quantity = 4m, notes = (string?)null, batchNumber = (string?)null, expiryDate = (DateOnly?)null } }
        }), HttpStatusCode.Created);

        Assert.Contains("only 4 can still be returned", await ErrorTextAsync(await ReturnPurchaseAsync(draftId, s.StaffToken, new PurchaseLine(draftLine, 5))));
        await JsonAsync(await ReturnPurchaseAsync(draftId, s.StaffToken, new PurchaseLine(draftLine, 3)), HttpStatusCode.Created);
        Assert.Equal(1, await OnHandAsync(s));
    }

    [Fact]
    public async Task PurchaseReturn_ConvertsTheOrderUnit()
    {
        var s = await NewScenarioAsync();
        var box = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("box"), name = "Box" }), HttpStatusCode.Created);
        var boxId = box.GetProperty("unitOfMeasureId").GetInt32();
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/inventory/products/{s.ProductId}/unit-conversions",
            new { unitOfMeasureId = boxId, conversionFactor = 12 }), HttpStatusCode.Created);

        var (orderId, lineId) = await ReceivedPurchaseAsync(s, boxId, new Entry(5));   // 60 pieces in
        Assert.Equal(60, await OnHandAsync(s));

        await JsonAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 2)), HttpStatusCode.Created);   // 2 boxes out
        Assert.Equal(36, await OnHandAsync(s));
    }

    [Fact]
    public async Task PurchaseReturn_OfATrackedProduct_GoesBackFromABatchTheOrderReceived_EvenWhenExpired()
    {
        var s = await NewScenarioAsync("BatchAndExpiry");
        var (orderId, lineId) = await ReceivedPurchaseAsync(s, null,
            new Entry(5, "GOOD", Today.AddDays(30)),
            new Entry(3, "LAPSED", Today.AddDays(-2)));

        // A batch from somewhere else is not this order's to return.
        await SeedAsync(s, 4, "ELSEWHERE", Today.AddDays(30));
        Assert.Contains("name the batch", await ErrorTextAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 1))));
        Assert.Contains("only 0 more", await ErrorTextAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 1, "ELSEWHERE"))));
        Assert.Contains("only 5 more", await ErrorTextAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 6, "GOOD"))));

        var returnable = await JsonAsync(await SendAsync(HttpMethod.Get, $"/purchasing/purchase-orders/{orderId}/returnable"), HttpStatusCode.OK);
        var batches = returnable.EnumerateArray().Single().GetProperty("batches").EnumerateArray()
            .ToDictionary(b => b.GetProperty("batchNumber").GetString()!, b => b.GetProperty("returnable").GetDecimal());
        Assert.Equal(new Dictionary<string, decimal> { ["GOOD"] = 5, ["LAPSED"] = 3 }, batches);

        // Expired goods are exactly what gets sent back to a supplier.
        await JsonAsync(await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 3, "LAPSED"), new PurchaseLine(lineId, 2, "GOOD")), HttpStatusCode.Created);
        Assert.Equal(0, await OnHandAsync(s, "LAPSED"));
        Assert.Equal(3, await OnHandAsync(s, "GOOD"));
        Assert.Equal(4, await OnHandAsync(s, "ELSEWHERE"));
    }

    [Fact]
    public async Task PurchaseReturn_FollowsTheOrdersBranch_AndConcurrentReturnsCannotExceedStock()
    {
        var s = await NewScenarioAsync();
        var (orderId, lineId) = await ReceivedPurchaseAsync(s, null, new Entry(10));

        Assert.Equal(HttpStatusCode.NotFound, (await ReturnPurchaseAsync(orderId, s.StrangerToken, new PurchaseLine(lineId, 1))).StatusCode);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            (await ReturnPurchaseAsync(orderId, s.StaffToken, new PurchaseLine(lineId, 4))).StatusCode));

        Assert.Equal(2, statuses.Count(x => x == HttpStatusCode.Created));
        Assert.Equal(2, await OnHandAsync(s));
        Assert.Equal(8, (await PurchaseOrderAsync(orderId)).GetProperty("lines")[0].GetProperty("quantityReturned").GetDecimal());
    }
}
