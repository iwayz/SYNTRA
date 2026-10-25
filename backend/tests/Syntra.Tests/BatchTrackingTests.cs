using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Syntra.Tests;

public class BatchTrackingTests : IClassFixture<TenantIsolationFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TenantIsolationFixture _fixture;

    public BatchTrackingTests(TenantIsolationFixture fixture)
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

    private record Setup(int ProductId, int UnitId, int WarehouseId, int SupplierId, int CustomerId);

    private async Task<Setup> NewSetupAsync(string mode)
    {
        var unit = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("u"), name = "Piece" }), HttpStatusCode.Created);
        var unitId = unit.GetProperty("unitOfMeasureId").GetInt32();
        var product = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/products",
            new { sku = Unique("sku"), name = "Widget", baseUnitOfMeasureId = unitId, batchTracking = mode }), HttpStatusCode.Created);
        var warehouse = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/warehouses", new { name = "WH", branchId = 1 }), HttpStatusCode.Created);
        var supplier = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/suppliers", new { name = "Supp" }), HttpStatusCode.Created);
        var customer = await JsonAsync(await SendAsync(HttpMethod.Post, "/sales/customers", new { name = "Cust" }), HttpStatusCode.Created);

        return new Setup(
            product.GetProperty("productId").GetInt32(),
            unitId,
            warehouse.GetProperty("warehouseId").GetInt32(),
            supplier.GetProperty("supplierId").GetInt32(),
            customer.GetProperty("customerId").GetInt32());
    }

    // A confirmed PO for `quantity`, returning the ids needed to receive against it.
    private async Task<(int OrderId, int LineId)> ConfirmedPoAsync(Setup s, decimal quantity)
    {
        var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", new
        {
            supplierId = s.SupplierId,
            warehouseId = s.WarehouseId,
            orderDate = DateTime.UtcNow,
            lines = new[] { new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity, unitPrice = 5 } }
        }), HttpStatusCode.Created);

        var orderId = order.GetProperty("purchaseOrderId").GetInt32();
        var lineId = order.GetProperty("lines")[0].GetProperty("purchaseOrderLineId").GetInt32();
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/confirm"), HttpStatusCode.OK);
        return (orderId, lineId);
    }

    private record Entry(decimal Quantity, string? Batch, DateOnly? Expiry);

    // Receives the given entries (one receipt line each) against a fresh PO sized to fit them.
    private async Task<HttpResponseMessage> ReceiveAsync(Setup s, params Entry[] entries)
    {
        var (orderId, lineId) = await ConfirmedPoAsync(s, entries.Sum(e => e.Quantity));

        return await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/goods-receipts", new
        {
            lines = entries.Select(e => new
            {
                purchaseOrderLineId = lineId,
                quantity = e.Quantity,
                notes = (string?)null,
                batchNumber = e.Batch,
                expiryDate = e.Expiry
            }).ToArray()
        });
    }

    private async Task StockInAsync(Setup s, decimal quantity, string batch, DateOnly? expiry) =>
        await JsonAsync(await ReceiveAsync(s, new Entry(quantity, batch, expiry)), HttpStatusCode.Created);

    private record Row(string? Batch, DateOnly? Expiry, decimal Quantity);

    private async Task<List<Row>> StockAsync(Setup s)
    {
        var balances = await JsonAsync(await SendAsync(HttpMethod.Get,
            $"/inventory/stock-balance?productId={s.ProductId}&warehouseId={s.WarehouseId}"), HttpStatusCode.OK);

        return balances.EnumerateArray().Select(b => new Row(
            b.GetProperty("batchNumber").GetString(),
            b.GetProperty("expiryDate").ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(b.GetProperty("expiryDate").GetString()!),
            b.GetProperty("quantityOnHand").GetDecimal())).ToList();
    }

    private static decimal Qty(IEnumerable<Row> rows, string? batch) =>
        rows.Where(r => string.Equals(r.Batch, batch, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity);

    // A draft SO with one line per quantity, confirmed; returns the confirm response.
    private async Task<HttpResponseMessage> SellAsync(Setup s, params decimal[] lineQuantities)
    {
        var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/sales/sales-orders", new
        {
            customerId = s.CustomerId,
            warehouseId = s.WarehouseId,
            orderDate = DateTime.UtcNow,
            lines = lineQuantities.Select(q => new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity = q, unitPrice = 5 }).ToArray()
        }), HttpStatusCode.Created);

        return await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{order.GetProperty("salesOrderId").GetInt32()}/confirm");
    }

    private Task<HttpResponseMessage> AdjustAsync(Setup s, decimal quantity, string? batch = null, DateOnly? expiry = null) =>
        SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", new
        {
            productId = s.ProductId,
            warehouseId = s.WarehouseId,
            quantity,
            notes = "test",
            batchNumber = batch,
            expiryDate = expiry
        });

    private async Task<string> ErrorTextAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- receiving ----

    [Fact]
    public async Task Receipt_EnforcesTheProductsTrackingMode()
    {
        var plain = await NewSetupAsync("None");
        Assert.Contains("does not track batches", await ErrorTextAsync(await ReceiveAsync(plain, new Entry(5, "B1", null))));

        var batchOnly = await NewSetupAsync("Batch");
        Assert.Contains("batch number is required", await ErrorTextAsync(await ReceiveAsync(batchOnly, new Entry(5, null, null))));
        Assert.Contains("batch only", await ErrorTextAsync(await ReceiveAsync(batchOnly, new Entry(5, "B1", Today.AddDays(30)))));
        await JsonAsync(await ReceiveAsync(batchOnly, new Entry(5, "B1", null)), HttpStatusCode.Created);

        var perishable = await NewSetupAsync("BatchAndExpiry");
        Assert.Contains("needs an expiry date", await ErrorTextAsync(await ReceiveAsync(perishable, new Entry(5, "B1", null))));
        await JsonAsync(await ReceiveAsync(perishable, new Entry(5, "B1", Today.AddDays(30))), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Receipt_CreatesBatchesAndAddsToExistingOnesCaseInsensitively()
    {
        var s = await NewSetupAsync("BatchAndExpiry");
        var expiry = Today.AddDays(30);

        await StockInAsync(s, 10, "LOT-A", expiry);
        await StockInAsync(s, 5, "lot-a", null);       // same batch, expiry taken from the existing one
        await StockInAsync(s, 7, "LOT-B", Today.AddDays(60));

        var rows = await StockAsync(s);
        Assert.Equal(2, rows.Count);
        Assert.Equal(15, Qty(rows, "LOT-A"));
        Assert.Equal(expiry, rows.Single(r => r.Batch == "LOT-A").Expiry);
        Assert.Equal(7, Qty(rows, "LOT-B"));

        // The same batch cannot be re-received with a different expiry.
        var conflict = await ErrorTextAsync(await ReceiveAsync(s, new Entry(1, "LOT-A", expiry.AddDays(1))));
        Assert.Contains("already exists with expiry", conflict);
    }

    [Fact]
    public async Task OneOrderLine_CanBeSplitAcrossBatchesInOneReceipt()
    {
        var s = await NewSetupAsync("BatchAndExpiry");

        await JsonAsync(await ReceiveAsync(s,
            new Entry(6, "SPLIT-1", Today.AddDays(20)),
            new Entry(4, "SPLIT-2", Today.AddDays(40))), HttpStatusCode.Created);

        var rows = await StockAsync(s);
        Assert.Equal(6, Qty(rows, "SPLIT-1"));
        Assert.Equal(4, Qty(rows, "SPLIT-2"));

        // The same batch twice in one receipt is still a duplicate.
        var duplicate = await ReceiveAsync(s, new Entry(1, "DUP", Today.AddDays(5)), new Entry(1, "dup", Today.AddDays(5)));
        Assert.Contains("only once per goods receipt", await ErrorTextAsync(duplicate));
    }

    [Fact]
    public async Task SplitReceipt_StillCannotExceedWhatIsOutstanding()
    {
        var s = await NewSetupAsync("Batch");
        var (orderId, lineId) = await ConfirmedPoAsync(s, 10);

        var response = await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/goods-receipts", new
        {
            lines = new[]
            {
                new { purchaseOrderLineId = lineId, quantity = 6m, notes = (string?)null, batchNumber = "X1", expiryDate = (DateOnly?)null },
                new { purchaseOrderLineId = lineId, quantity = 6m, notes = (string?)null, batchNumber = "X2", expiryDate = (DateOnly?)null }
            }
        });

        Assert.Contains("only 4 is outstanding", await ErrorTextAsync(response));
        Assert.Empty(await StockAsync(s));
    }

    [Fact]
    public async Task ReceiveAll_IsRefusedForBatchTrackedProducts()
    {
        var tracked = await NewSetupAsync("Batch");
        var (trackedOrder, _) = await ConfirmedPoAsync(tracked, 5);
        Assert.Contains("receive all", await ErrorTextAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{trackedOrder}/receive")));

        var plain = await NewSetupAsync("None");
        var (plainOrder, _) = await ConfirmedPoAsync(plain, 5);
        await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{plainOrder}/receive"), HttpStatusCode.OK);
    }

    // ---- selling (FEFO) ----

    [Fact]
    public async Task Sale_IssuesTheEarliestExpiryFirstAndUndatedStockLast()
    {
        // Existing untracked stock becomes the undated OPENING batch.
        var s = await NewSetupAsync("None");
        await JsonAsync(await AdjustAsync(s, 4), HttpStatusCode.Created);
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "BatchAndExpiry" }), HttpStatusCode.OK);

        await StockInAsync(s, 5, "LATE", Today.AddDays(40));
        await StockInAsync(s, 5, "SOON", Today.AddDays(10));

        // 8 units: all 5 of SOON, then 3 of LATE; OPENING (no expiry) untouched.
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s, 8)).StatusCode);
        var rows = await StockAsync(s);
        Assert.Equal(0, Qty(rows, "SOON"));
        Assert.DoesNotContain(rows, r => r.Batch == "SOON");   // used-up batches are not listed
        Assert.Equal(2, Qty(rows, "LATE"));
        Assert.Equal(4, Qty(rows, "OPENING"));

        // The remaining 6: LATE's 2, then OPENING's 4.
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s, 6)).StatusCode);
        Assert.Empty(await StockAsync(s));
    }

    [Fact]
    public async Task Sale_NeverUsesExpiredBatches()
    {
        var s = await NewSetupAsync("BatchAndExpiry");
        await StockInAsync(s, 5, "OLD", Today.AddDays(-1));
        await StockInAsync(s, 3, "FRESH", Today.AddDays(30));
        await StockInAsync(s, 2, "TODAY", Today);

        var tooMany = await ErrorTextAsync(await SellAsync(s, 6));
        Assert.Contains("available 5", tooMany);
        Assert.Contains("5 is expired", tooMany);

        // TODAY (expires today) and FRESH are sellable, OLD stays untouched.
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s, 5)).StatusCode);
        var rows = await StockAsync(s);
        Assert.Equal(5, Qty(rows, "OLD"));
        Assert.Equal(0, rows.Count(r => r.Batch != "OLD"));
    }

    [Fact]
    public async Task Sale_MultipleLinesShareOnePoolAndAreAllOrNothing()
    {
        var s = await NewSetupAsync("BatchAndExpiry");
        await StockInAsync(s, 4, "M1", Today.AddDays(10));
        await StockInAsync(s, 1, "M2", Today.AddDays(20));   // 5 sellable in total

        // 3 + 3 = 6 > 5: neither line may go through.
        Assert.Contains("available 2", await ErrorTextAsync(await SellAsync(s, 3, 3)));
        var untouched = await StockAsync(s);
        Assert.Equal(4, Qty(untouched, "M1"));
        Assert.Equal(1, Qty(untouched, "M2"));

        // 3 + 2 = 5 fits exactly: M1 gives 4, M2 gives 1.
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s, 3, 2)).StatusCode);
        Assert.Empty(await StockAsync(s));
    }

    [Fact]
    public async Task ConcurrentSales_NeverTakeABatchBelowZero()
    {
        for (var round = 0; round < 3; round++)
        {
            var s = await NewSetupAsync("BatchAndExpiry");
            await StockInAsync(s, 5, "C1", Today.AddDays(10));
            await StockInAsync(s, 5, "C2", Today.AddDays(20));

            var statuses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
                (await SellAsync(s, 3)).StatusCode));

            Assert.Equal(3, statuses.Count(x => x == HttpStatusCode.OK));

            var rows = await StockAsync(s);
            Assert.All(rows, r => Assert.True(r.Quantity >= 0));
            Assert.Equal(1, rows.Sum(r => r.Quantity));
        }
    }

    // ---- switching tracking on ----

    [Fact]
    public async Task EnablingTracking_MovesExistingStockIntoAnOpeningBatchWithoutChangingBalances()
    {
        var s = await NewSetupAsync("None");
        await JsonAsync(await AdjustAsync(s, 10), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s, 3)).StatusCode);   // some history before tracking
        Assert.Null((await StockAsync(s)).Single().Batch);

        var enabled = await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "Batch" }), HttpStatusCode.OK);
        Assert.Equal("Batch", enabled.GetProperty("batchTracking").GetString());

        var rows = await StockAsync(s);
        var opening = Assert.Single(rows);
        Assert.Equal("OPENING", opening.Batch);
        Assert.Equal(7, opening.Quantity);

        // Every movement of the product now has a batch.
        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?productId={s.ProductId}"), HttpStatusCode.OK);
        Assert.All(movements.EnumerateArray(), m => Assert.Equal("OPENING", m.GetProperty("batch").GetProperty("batchNumber").GetString()));

        // Repeating the same mode is a no-op; tracking cannot be turned off.
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "Batch" }), HttpStatusCode.OK);
        Assert.Contains("cannot be turned off", await ErrorTextAsync(
            await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "None" })));

        // Moving between the tracked modes is allowed.
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "BatchAndExpiry" }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnablingTracking_OnAProductWithNoHistory_CreatesNoOpeningBatch()
    {
        var s = await NewSetupAsync("None");
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "Batch" }), HttpStatusCode.OK);

        var batches = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/batches?productId={s.ProductId}"), HttpStatusCode.OK);
        Assert.Equal(0, batches.GetArrayLength());
    }

    [Fact]
    public async Task EnablingTracking_IsAdminOnly_AndKnowsMissingProducts()
    {
        var s = await NewSetupAsync("None");

        var email = $"staff-{Guid.NewGuid():N}@isolation-test.local";
        await JsonAsync(await SendAsync(HttpMethod.Post, "/tenancy/users",
            new { email, password = "P@ssw0rd123!", isAdmin = false, branchIds = new[] { 1 } }), HttpStatusCode.Created);

        var login = _fixture.CreateRequest(HttpMethod.Post, _fixture.TenantASlug, "/auth/login");
        login.Content = JsonContent.Create(new { email, password = "P@ssw0rd123!" });
        var staffToken = (await (await _fixture.Client.SendAsync(login)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put,
            $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "Batch" }, staffToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put,
            "/inventory/products/999999/batch-tracking", new { mode = "Batch" })).StatusCode);
    }

    // ---- adjustments ----

    [Fact]
    public async Task Adjustments_WorkPerBatchAndCanWriteOffExpiredStock()
    {
        var s = await NewSetupAsync("BatchAndExpiry");

        Assert.Contains("batch number is required", await ErrorTextAsync(await AdjustAsync(s, 5)));
        Assert.Contains("needs an expiry date", await ErrorTextAsync(await AdjustAsync(s, 5, "ADJ-1")));

        await JsonAsync(await AdjustAsync(s, 3, "ADJ-1", Today.AddDays(-5)), HttpStatusCode.Created);   // already expired
        await JsonAsync(await AdjustAsync(s, 10, "ADJ-2", Today.AddDays(20)), HttpStatusCode.Created);

        // The limit is per batch: ADJ-1 only has 3 even though the warehouse holds 13.
        Assert.Contains("only 3 is on hand in batch 'ADJ-1'", await ErrorTextAsync(await AdjustAsync(s, -5, "ADJ-1")));
        Assert.Contains("does not exist", await ErrorTextAsync(await AdjustAsync(s, -1, "NOPE")));
        Assert.Contains("name the batch", await ErrorTextAsync(await AdjustAsync(s, -1)));

        // Writing the expired batch off is exactly how it leaves the books.
        await JsonAsync(await AdjustAsync(s, -3, "adj-1"), HttpStatusCode.Created);
        var rows = await StockAsync(s);
        Assert.DoesNotContain(rows, r => r.Batch == "ADJ-1");
        Assert.Equal(10, Qty(rows, "ADJ-2"));
    }

    // ---- batch master data ----

    [Fact]
    public async Task BatchExpiry_CanBeCorrectedByAnAdminOnlyForExpiryTrackedProducts()
    {
        var perishable = await NewSetupAsync("BatchAndExpiry");
        await StockInAsync(perishable, 5, "TYPO", Today.AddDays(30));

        var batches = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/batches?productId={perishable.ProductId}"), HttpStatusCode.OK);
        var batchId = batches[0].GetProperty("productBatchId").GetInt32();

        var fixedBatch = await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/batches/{batchId}", new { expiryDate = Today.AddDays(90) }), HttpStatusCode.OK);
        Assert.Equal(Today.AddDays(90).ToString("yyyy-MM-dd"), fixedBatch.GetProperty("expiryDate").GetString());
        Assert.Equal("TYPO", fixedBatch.GetProperty("batchNumber").GetString());

        var batchOnly = await NewSetupAsync("Batch");
        await StockInAsync(batchOnly, 5, "NODATE", null);
        var other = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/batches?productId={batchOnly.ProductId}"), HttpStatusCode.OK);
        var otherId = other[0].GetProperty("productBatchId").GetInt32();
        Assert.Contains("does not track expiry", await ErrorTextAsync(
            await SendAsync(HttpMethod.Put, $"/inventory/batches/{otherId}", new { expiryDate = Today.AddDays(5) })));
    }
}
