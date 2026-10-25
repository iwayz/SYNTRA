using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Syntra.Tests;

public class StockCountTests : IClassFixture<TenantIsolationFixture>
{
    private const string Password = "P@ssw0rd123!";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TenantIsolationFixture _fixture;

    public StockCountTests(TenantIsolationFixture fixture)
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

    // A product, a branch with a warehouse (and a branch user), another warehouse to
    // move stock to, and an unrelated branch user.
    private record Scenario(
        int ProductId,
        int UnitId,
        int BranchId,
        int Warehouse,
        int OtherWarehouse,
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

        var (branchId, warehouse) = await BranchWithWarehouseAsync("C");
        var (otherBranch, otherWarehouse) = await BranchWithWarehouseAsync("O");

        return new Scenario(
            product.GetProperty("productId").GetInt32(), unitId, branchId, warehouse, otherWarehouse,
            customer.GetProperty("customerId").GetInt32(), supplier.GetProperty("supplierId").GetInt32(),
            await StaffTokenAsync(branchId), await StaffTokenAsync(otherBranch));
    }

    private Task<HttpResponseMessage> AdjustAsync(Scenario s, decimal quantity, string? batch = null, DateOnly? expiry = null, int? warehouse = null) =>
        SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", new
        {
            productId = s.ProductId,
            warehouseId = warehouse ?? s.Warehouse,
            quantity,
            notes = "seed",
            batchNumber = batch,
            expiryDate = expiry
        });

    private async Task SeedAsync(Scenario s, decimal quantity, string? batch = null, DateOnly? expiry = null) =>
        await JsonAsync(await AdjustAsync(s, quantity, batch, expiry), HttpStatusCode.Created);

    private Task<HttpResponseMessage> CreateCountAsync(Scenario s, string? token = null, int? warehouse = null) =>
        SendAsync(HttpMethod.Post, "/inventory/stock-counts", new { warehouseId = warehouse ?? s.Warehouse, notes = (string?)null }, token ?? s.StaffToken);

    private async Task<int> OpenCountAsync(Scenario s)
    {
        var detail = await JsonAsync(await CreateCountAsync(s), HttpStatusCode.Created);
        return detail.GetProperty("summary").GetProperty("stockCountId").GetInt32();
    }

    private async Task<JsonElement> DetailAsync(int id, string? token = null) =>
        await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-counts/{id}", token: token), HttpStatusCode.OK);

    private static JsonElement RowOf(JsonElement detail, string? batch = null) =>
        detail.GetProperty("rows").EnumerateArray().Single(r =>
            batch is null
                ? r.GetProperty("batchNumber").ValueKind == JsonValueKind.Null
                : r.GetProperty("batchNumber").GetString() == batch);

    private static int RowId(JsonElement detail, string? batch = null) => RowOf(detail, batch).GetProperty("stockCountLineId").GetInt32();

    private Task<HttpResponseMessage> CountRowAsync(int countId, int rowId, decimal? quantity, string? token = null, Scenario? s = null) =>
        SendAsync(HttpMethod.Put, $"/inventory/stock-counts/{countId}/rows/{rowId}", new { countedQuantity = quantity }, token ?? s?.StaffToken);

    private Task<HttpResponseMessage> PostCountAsync(int id, string? token = null) =>
        SendAsync(HttpMethod.Post, $"/inventory/stock-counts/{id}/post", token: token);

    private Task<HttpResponseMessage> CancelCountAsync(int id, string? token = null) =>
        SendAsync(HttpMethod.Post, $"/inventory/stock-counts/{id}/cancel", token: token);

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

    // ---- the sheet ----

    [Fact]
    public async Task Create_SnapshotsWhatTheWarehouseHolds()
    {
        var plain = await NewScenarioAsync();
        await SeedAsync(plain, 10);

        var countId = await OpenCountAsync(plain);
        var detail = await DetailAsync(countId);

        Assert.Equal("Open", detail.GetProperty("summary").GetProperty("status").GetString());
        var row = Assert.Single(detail.GetProperty("rows").EnumerateArray());
        Assert.Equal(10, row.GetProperty("expectedQuantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("countedQuantity").ValueKind);
        Assert.Equal(0, detail.GetProperty("summary").GetProperty("countedCount").GetInt32());

        // A tracked product gets one row per batch holding stock - expired ones included -
        // and a used-up batch gets none.
        var tracked = await NewScenarioAsync("BatchAndExpiry");
        await SeedAsync(tracked, 5, "B1", Today.AddDays(30));
        await SeedAsync(tracked, 3, "OLD", Today.AddDays(-2));
        await SeedAsync(tracked, 2, "GONE", Today.AddDays(60));
        await JsonAsync(await AdjustAsync(tracked, -2, "GONE"), HttpStatusCode.Created);

        var trackedDetail = await DetailAsync(await OpenCountAsync(tracked));
        var batches = trackedDetail.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("batchNumber").GetString()).Order().ToList();
        Assert.Equal(["B1", "OLD"], batches);
    }

    [Fact]
    public async Task OnlyOneOpenCountPerWarehouse()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 5);
        var first = await OpenCountAsync(s);

        Assert.Contains("already has an open count", await ErrorTextAsync(await CreateCountAsync(s)));

        await JsonAsync(await CancelCountAsync(first, s.StaffToken), HttpStatusCode.OK);
        await JsonAsync(await CreateCountAsync(s), HttpStatusCode.Created);
    }

    // ---- counting ----

    [Fact]
    public async Task CountingARow_RecordsItAndCanBeCleared()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);
        var rowId = RowId(await DetailAsync(id));

        var counted = await JsonAsync(await CountRowAsync(id, rowId, 7, s: s), HttpStatusCode.OK);
        var row = RowOf(counted);
        Assert.Equal(7, row.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(10, row.GetProperty("systemQuantityAtCount").GetDecimal());
        Assert.Equal(-3, row.GetProperty("variance").GetDecimal());
        Assert.False(row.GetProperty("isStale").GetBoolean());
        Assert.Equal(1, counted.GetProperty("summary").GetProperty("countedCount").GetInt32());

        var cleared = await JsonAsync(await CountRowAsync(id, rowId, null, s: s), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, RowOf(cleared).GetProperty("countedQuantity").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await CountRowAsync(id, rowId, -1, s: s)).StatusCode);
        Assert.Equal(10, await OnHandAsync(s));   // counting alone never changes stock
    }

    // ---- posting ----

    [Fact]
    public async Task Posting_MakesTheBooksMatchTheCount_AndLeavesUncountedRowsAlone()
    {
        var s = await NewScenarioAsync("Batch");
        await SeedAsync(s, 10, "KEEP");
        await SeedAsync(s, 8, "LESS");
        await SeedAsync(s, 6, "MORE");
        await SeedAsync(s, 4, "SKIP");
        var id = await OpenCountAsync(s);
        var detail = await DetailAsync(id);

        await JsonAsync(await CountRowAsync(id, RowId(detail, "KEEP"), 10, s: s), HttpStatusCode.OK);   // no difference
        await JsonAsync(await CountRowAsync(id, RowId(detail, "LESS"), 5, s: s), HttpStatusCode.OK);    // -3
        await JsonAsync(await CountRowAsync(id, RowId(detail, "MORE"), 9, s: s), HttpStatusCode.OK);    // +3
        // SKIP is never counted.

        var posted = await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);

        Assert.Equal("Posted", posted.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal(10, await OnHandAsync(s, "KEEP"));
        Assert.Equal(5, await OnHandAsync(s, "LESS"));
        Assert.Equal(9, await OnHandAsync(s, "MORE"));
        Assert.Equal(4, await OnHandAsync(s, "SKIP"));

        Assert.Equal(-3, RowOf(posted, "LESS").GetProperty("postedVariance").GetDecimal());
        Assert.Equal(3, RowOf(posted, "MORE").GetProperty("postedVariance").GetDecimal());
        Assert.Equal(0, RowOf(posted, "KEEP").GetProperty("postedVariance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, RowOf(posted, "SKIP").GetProperty("postedVariance").ValueKind);

        // One StockCount movement per row that changed, tied to the count.
        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?referenceId={id}&productId={s.ProductId}"), HttpStatusCode.OK);
        var counts = movements.EnumerateArray().Where(m => m.GetProperty("movementType").GetString() == "StockCount").ToList();
        Assert.Equal(2, counts.Count);

        // A posted count is final.
        Assert.Contains("Only an open count", await ErrorTextAsync(await PostCountAsync(id)));
        Assert.Contains("Only an open count", await ErrorTextAsync(await CancelCountAsync(id, s.StaffToken)));
        Assert.Equal(HttpStatusCode.BadRequest, (await CountRowAsync(id, RowId(detail, "KEEP"), 1, s: s)).StatusCode);
    }

    [Fact]
    public async Task CountingZero_ZeroesTheStock_AndNothingCountedCannotBePosted()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 6);
        var id = await OpenCountAsync(s);

        Assert.Contains("Nothing has been counted", await ErrorTextAsync(await PostCountAsync(id)));

        await JsonAsync(await CountRowAsync(id, RowId(await DetailAsync(id)), 0, s: s), HttpStatusCode.OK);
        await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);

        Assert.Equal(0, await OnHandAsync(s));
    }

    [Fact]
    public async Task FoundStock_CanBeAddedForAnExistingBatchOrAnUntrackedProduct()
    {
        // Untracked product with nothing on the books: found 4.
        var plain = await NewScenarioAsync();
        var plainId = await OpenCountAsync(plain);
        Assert.Empty((await DetailAsync(plainId)).GetProperty("rows").EnumerateArray());

        var found = await JsonAsync(await SendAsync(HttpMethod.Post, $"/inventory/stock-counts/{plainId}/rows",
            new { productId = plain.ProductId, batchNumber = (string?)null, countedQuantity = 4 }, plain.StaffToken), HttpStatusCode.OK);
        Assert.Equal(0, RowOf(found).GetProperty("expectedQuantity").GetDecimal());
        await JsonAsync(await PostCountAsync(plainId), HttpStatusCode.OK);
        Assert.Equal(4, await OnHandAsync(plain));

        // Tracked product: the batch must already exist, and a thing appears once per count.
        var tracked = await NewScenarioAsync("BatchAndExpiry");
        await SeedAsync(tracked, 5, "ELSEWHERE", Today.AddDays(30));
        await JsonAsync(await AdjustAsync(tracked, 2, "ELSEWHERE", warehouse: tracked.OtherWarehouse), HttpStatusCode.Created);
        var trackedId = await OpenCountAsync(tracked);

        object Add(string? batch, decimal qty) => new { productId = tracked.ProductId, batchNumber = batch, countedQuantity = qty };
        Assert.Contains("does not exist", await ErrorTextAsync(
            await SendAsync(HttpMethod.Post, $"/inventory/stock-counts/{trackedId}/rows", Add("BRAND-NEW", 3), tracked.StaffToken)));
        Assert.Contains("name the batch", await ErrorTextAsync(
            await SendAsync(HttpMethod.Post, $"/inventory/stock-counts/{trackedId}/rows", Add(null, 3), tracked.StaffToken)));
        Assert.Contains("already on the count sheet", await ErrorTextAsync(
            await SendAsync(HttpMethod.Post, $"/inventory/stock-counts/{trackedId}/rows", Add("ELSEWHERE", 3), tracked.StaffToken)));
    }

    [Fact]
    public async Task AnExpiredBatch_IsWrittenOffByCountingItAtZero()
    {
        var s = await NewScenarioAsync("BatchAndExpiry");
        await SeedAsync(s, 5, "OLD", Today.AddDays(-3));
        await SeedAsync(s, 7, "FRESH", Today.AddDays(30));
        var id = await OpenCountAsync(s);
        var detail = await DetailAsync(id);

        await JsonAsync(await CountRowAsync(id, RowId(detail, "OLD"), 0, s: s), HttpStatusCode.OK);
        await JsonAsync(await CountRowAsync(id, RowId(detail, "FRESH"), 7, s: s), HttpStatusCode.OK);
        await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);

        Assert.Equal(0, await OnHandAsync(s, "OLD"));
        Assert.Equal(7, await OnHandAsync(s, "FRESH"));
    }

    // ---- stock moving during a count ----

    // Each kind of stock movement, done to the counted product after it was counted.
    private async Task MoveStockAsync(Scenario s, string kind)
    {
        switch (kind)
        {
            case "adjustment":
                await JsonAsync(await AdjustAsync(s, 1), HttpStatusCode.Created);
                break;

            case "sale":
            {
                var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/sales/sales-orders", new
                {
                    customerId = s.CustomerId,
                    warehouseId = s.Warehouse,
                    orderDate = DateTime.UtcNow,
                    lines = new[] { new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity = 1, unitPrice = 5 } }
                }), HttpStatusCode.Created);
                await JsonAsync(await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{order.GetProperty("salesOrderId").GetInt32()}/confirm"), HttpStatusCode.OK);
                break;
            }

            case "receipt":
            {
                var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", new
                {
                    supplierId = s.SupplierId,
                    warehouseId = s.Warehouse,
                    orderDate = DateTime.UtcNow,
                    lines = new[] { new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity = 1, unitPrice = 5 } }
                }), HttpStatusCode.Created);
                var orderId = order.GetProperty("purchaseOrderId").GetInt32();
                await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/confirm"), HttpStatusCode.OK);
                await JsonAsync(await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{orderId}/receive"), HttpStatusCode.OK);
                break;
            }

            case "transfer":
            {
                var transfer = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/transfers", new
                {
                    fromWarehouseId = s.Warehouse,
                    toWarehouseId = s.OtherWarehouse,
                    notes = (string?)null,
                    lines = new[] { new { productId = s.ProductId, quantity = 1, batchNumber = (string?)null } }
                }), HttpStatusCode.Created);
                await JsonAsync(await SendAsync(HttpMethod.Post, $"/inventory/transfers/{transfer.GetProperty("stockTransferId").GetInt32()}/dispatch"), HttpStatusCode.OK);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData("adjustment")]
    [InlineData("sale")]
    [InlineData("receipt")]
    [InlineData("transfer")]
    public async Task ARowThatStockMovedOnFromAfterItWasCounted_MustBeCountedAgain(string kind)
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);
        var rowId = RowId(await DetailAsync(id));

        await JsonAsync(await CountRowAsync(id, rowId, 10, s: s), HttpStatusCode.OK);
        await MoveStockAsync(s, kind);

        var detail = await DetailAsync(id);
        Assert.True(RowOf(detail).GetProperty("isStale").GetBoolean());

        var error = await ErrorTextAsync(await PostCountAsync(id));
        Assert.Contains("counted again", error);
        Assert.Contains("Widget", error);

        // Counting it again (against the books as they are now) makes it postable.
        var current = RowOf(detail).GetProperty("currentQuantity").GetDecimal();
        var recounted = await JsonAsync(await CountRowAsync(id, rowId, current, s: s), HttpStatusCode.OK);
        Assert.False(RowOf(recounted).GetProperty("isStale").GetBoolean());
        await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);
        Assert.Equal(current, await OnHandAsync(s));
    }

    [Fact]
    public async Task StockThatMovedBeforeTheCount_DoesNotMakeARowStale()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);

        await SeedAsync(s, 5);   // moves after the sheet was created ...
        var rowId = RowId(await DetailAsync(id));
        var counted = await JsonAsync(await CountRowAsync(id, rowId, 15, s: s), HttpStatusCode.OK);   // ... but before this row was counted

        Assert.False(RowOf(counted).GetProperty("isStale").GetBoolean());
        Assert.Equal(15, RowOf(counted).GetProperty("systemQuantityAtCount").GetDecimal());
        await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);
        Assert.Equal(15, await OnHandAsync(s));
    }

    [Fact]
    public async Task APostRacingASale_NeverCorruptsTheBooks()
    {
        for (var round = 0; round < 4; round++)
        {
            var s = await NewScenarioAsync();
            await SeedAsync(s, 10);
            var id = await OpenCountAsync(s);
            await JsonAsync(await CountRowAsync(id, RowId(await DetailAsync(id)), 7, s: s), HttpStatusCode.OK);

            var order = await JsonAsync(await SendAsync(HttpMethod.Post, "/sales/sales-orders", new
            {
                customerId = s.CustomerId,
                warehouseId = s.Warehouse,
                orderDate = DateTime.UtcNow,
                lines = new[] { new { productId = s.ProductId, unitOfMeasureId = s.UnitId, quantity = 2, unitPrice = 5 } }
            }), HttpStatusCode.Created);
            var orderId = order.GetProperty("salesOrderId").GetInt32();

            var results = await Task.WhenAll(
                PostCountAsync(id),
                SendAsync(HttpMethod.Post, $"/sales/sales-orders/{orderId}/confirm"));

            Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
            var posted = results[0].StatusCode == HttpStatusCode.OK;

            // Post first: books become 7, then the sale takes 2 -> 5.
            // Sale first: the counted row is stale, the post is refused -> 8.
            Assert.Equal(posted ? 5 : 8, await OnHandAsync(s));
            if (!posted)
            {
                Assert.Contains("counted again", await results[0].Content.ReadAsStringAsync());
            }
        }
    }

    // ---- who may do what ----

    [Fact]
    public async Task BranchStaffCountAndCancel_ButOnlyAdminsPost()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);   // created by branch staff
        await JsonAsync(await CountRowAsync(id, RowId(await DetailAsync(id)), 6, s: s), HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostCountAsync(id, s.StaffToken)).StatusCode);
        Assert.Equal(10, await OnHandAsync(s));

        // The admin reviews and posts.
        await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);
        Assert.Equal(6, await OnHandAsync(s));
    }

    [Fact]
    public async Task ACountIsInvisibleToOtherBranches()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);
        var rowId = RowId(await DetailAsync(id));

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/inventory/stock-counts/{id}", token: s.StrangerToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CountRowAsync(id, rowId, 1, s.StrangerToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelCountAsync(id, s.StrangerToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCountAsync(id, s.StrangerToken)).StatusCode);   // not an admin

        // ... and can't start a count on a warehouse that isn't theirs.
        Assert.Contains("does not exist", await ErrorTextAsync(await CreateCountAsync(s, s.StrangerToken)));

        var strangerList = await JsonAsync(await SendAsync(HttpMethod.Get, "/inventory/stock-counts", token: s.StrangerToken), HttpStatusCode.OK);
        Assert.DoesNotContain(strangerList.EnumerateArray(), c => c.GetProperty("stockCountId").GetInt32() == id);
        var ownList = await JsonAsync(await SendAsync(HttpMethod.Get, "/inventory/stock-counts", token: s.StaffToken), HttpStatusCode.OK);
        Assert.Contains(ownList.EnumerateArray(), c => c.GetProperty("stockCountId").GetInt32() == id);
    }

    // ---- other rules ----

    [Fact]
    public async Task InactiveBranch_BlocksNewCountsButNotFinishingOne()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);
        await JsonAsync(await CountRowAsync(id, RowId(await DetailAsync(id)), 8, s: s), HttpStatusCode.OK);

        var branch = await JsonAsync(await SendAsync(HttpMethod.Get, $"/tenancy/branches/{s.BranchId}"), HttpStatusCode.OK);
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/tenancy/branches/{s.BranchId}", new
        {
            code = branch.GetProperty("code").GetString(),
            name = branch.GetProperty("name").GetString(),
            isActive = false
        }), HttpStatusCode.OK);

        // The open count can still be posted ...
        await JsonAsync(await PostCountAsync(id), HttpStatusCode.OK);
        Assert.Equal(8, await OnHandAsync(s));

        // ... but a new one cannot be started.
        Assert.Contains("inactive", await ErrorTextAsync(await CreateCountAsync(s)));
    }

    [Fact]
    public async Task SwitchingOnBatchTracking_DuringACount_InvalidatesTheOldSheet()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await OpenCountAsync(s);
        var rowId = RowId(await DetailAsync(id));

        await JsonAsync(await SendAsync(HttpMethod.Put, $"/inventory/products/{s.ProductId}/batch-tracking", new { mode = "Batch" }), HttpStatusCode.OK);

        Assert.Contains("batch tracking changed", await ErrorTextAsync(await CountRowAsync(id, rowId, 9, s: s)));
    }
}
