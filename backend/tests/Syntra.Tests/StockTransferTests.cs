using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Syntra.Tests;

public class StockTransferTests : IClassFixture<TenantIsolationFixture>
{
    private const string Password = "P@ssw0rd123!";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TenantIsolationFixture _fixture;

    public StockTransferTests(TenantIsolationFixture fixture)
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

    // One product, and three branches each with a warehouse and a staff user:
    // the source (S), the destination (D) and an unrelated one (X).
    private record Scenario(
        int ProductId,
        int UnitId,
        int SourceWarehouse,
        int DestWarehouse,
        int OtherWarehouse,
        int SourceBranch,
        int DestBranch,
        string SourceToken,
        string DestToken,
        string OtherToken);

    private async Task<string> StaffTokenAsync(int branchId)
    {
        var email = $"staff-{Guid.NewGuid():N}@isolation-test.local";
        await JsonAsync(await SendAsync(HttpMethod.Post, "/tenancy/users",
            new { email, password = Password, isAdmin = false, branchIds = new[] { branchId } }), HttpStatusCode.Created);

        var login = _fixture.CreateRequest(HttpMethod.Post, _fixture.TenantASlug, "/auth/login");
        login.Content = JsonContent.Create(new { email, password = Password });
        var response = await JsonAsync(await _fixture.Client.SendAsync(login), HttpStatusCode.OK);
        return response.GetProperty("token").GetString()!;
    }

    private async Task<(int BranchId, int WarehouseId)> BranchWithWarehouseAsync(string label)
    {
        var branch = await JsonAsync(await SendAsync(HttpMethod.Post, "/tenancy/branches", new { code = Unique(label), name = $"Branch {label}" }), HttpStatusCode.Created);
        var branchId = branch.GetProperty("branchId").GetInt32();
        var warehouse = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/warehouses", new { name = $"WH {label}", branchId }), HttpStatusCode.Created);
        return (branchId, warehouse.GetProperty("warehouseId").GetInt32());
    }

    private async Task<Scenario> NewScenarioAsync(string trackingMode = "None")
    {
        var unit = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/units-of-measure", new { code = Unique("u"), name = "Piece" }), HttpStatusCode.Created);
        var unitId = unit.GetProperty("unitOfMeasureId").GetInt32();
        var product = await JsonAsync(await SendAsync(HttpMethod.Post, "/inventory/products",
            new { sku = Unique("sku"), name = "Widget", baseUnitOfMeasureId = unitId, batchTracking = trackingMode }), HttpStatusCode.Created);

        var (sourceBranch, sourceWh) = await BranchWithWarehouseAsync("S");
        var (destBranch, destWh) = await BranchWithWarehouseAsync("D");
        var (otherBranch, otherWh) = await BranchWithWarehouseAsync("X");

        return new Scenario(
            product.GetProperty("productId").GetInt32(), unitId, sourceWh, destWh, otherWh, sourceBranch, destBranch,
            await StaffTokenAsync(sourceBranch), await StaffTokenAsync(destBranch), await StaffTokenAsync(otherBranch));
    }

    private Task<HttpResponseMessage> AdjustAsync(Scenario s, int warehouseId, decimal quantity, string? batch = null, DateOnly? expiry = null) =>
        SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", new
        {
            productId = s.ProductId,
            warehouseId,
            quantity,
            notes = "seed",
            batchNumber = batch,
            expiryDate = expiry
        });

    private async Task SeedAsync(Scenario s, decimal quantity) =>
        await JsonAsync(await AdjustAsync(s, s.SourceWarehouse, quantity), HttpStatusCode.Created);

    private record Line(decimal Quantity, string? Batch = null);

    private Task<HttpResponseMessage> CreateAsync(Scenario s, string? token, int from, int to, params Line[] lines) =>
        SendAsync(HttpMethod.Post, "/inventory/transfers", new
        {
            fromWarehouseId = from,
            toWarehouseId = to,
            notes = (string?)null,
            lines = lines.Select(l => new { productId = s.ProductId, quantity = l.Quantity, batchNumber = l.Batch }).ToArray()
        }, token);

    private async Task<int> DraftAsync(Scenario s, params Line[] lines)
    {
        var created = await JsonAsync(await CreateAsync(s, s.SourceToken, s.SourceWarehouse, s.DestWarehouse, lines), HttpStatusCode.Created);
        return created.GetProperty("stockTransferId").GetInt32();
    }

    private Task<HttpResponseMessage> DispatchAsync(int id, string? token) =>
        SendAsync(HttpMethod.Post, $"/inventory/transfers/{id}/dispatch", token: token);

    private Task<HttpResponseMessage> CancelAsync(int id, string? token) =>
        SendAsync(HttpMethod.Post, $"/inventory/transfers/{id}/cancel", token: token);

    private Task<HttpResponseMessage> CloseAsync(int id, string? token) =>
        SendAsync(HttpMethod.Post, $"/inventory/transfers/{id}/close", token: token);

    private async Task<JsonElement> GetAsync(int id, string? token = null) =>
        await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/transfers/{id}", token: token), HttpStatusCode.OK);

    private static List<JsonElement> Allocations(JsonElement transfer) =>
        transfer.GetProperty("lines").EnumerateArray()
            .SelectMany(l => l.GetProperty("allocations").EnumerateArray()).ToList();

    // Receives `quantity` against the allocation with the given batch (null = unbatched).
    private async Task<HttpResponseMessage> ReceiveAsync(int id, string? token, params (string? Batch, decimal Quantity)[] parts)
    {
        var transfer = await GetAsync(id);
        var allocations = Allocations(transfer);

        var lines = parts.Select(p =>
        {
            var allocation = allocations.First(a =>
                p.Batch is null
                    ? a.GetProperty("batch").ValueKind == JsonValueKind.Null
                    : a.GetProperty("batch").GetProperty("batchNumber").GetString() == p.Batch);

            return new { stockTransferAllocationId = allocation.GetProperty("stockTransferAllocationId").GetInt32(), quantity = p.Quantity };
        }).ToArray();

        return await SendAsync(HttpMethod.Post, $"/inventory/transfers/{id}/receive", new { lines }, token);
    }

    private record Row(string? Batch, decimal Quantity);

    private async Task<List<Row>> StockAsync(Scenario s, int warehouseId)
    {
        var balances = await JsonAsync(await SendAsync(HttpMethod.Get,
            $"/inventory/stock-balance?productId={s.ProductId}&warehouseId={warehouseId}"), HttpStatusCode.OK);

        return balances.EnumerateArray().Select(b => new Row(
            b.GetProperty("batchNumber").GetString(),
            b.GetProperty("quantityOnHand").GetDecimal())).ToList();
    }

    private async Task<decimal> OnHandAsync(Scenario s, int warehouseId) => (await StockAsync(s, warehouseId)).Sum(r => r.Quantity);

    private async Task<decimal> InTransitAsync(Scenario s)
    {
        var rows = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-in-transit?productId={s.ProductId}"), HttpStatusCode.OK);
        return rows.EnumerateArray().Sum(r => r.GetProperty("quantity").GetDecimal());
    }

    private async Task<string> ErrorTextAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.BadRequest)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- the basic flow ----

    [Fact]
    public async Task Transfer_MovesStockThroughTransitToTheDestination()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(6));

        // A draft moves and reserves nothing.
        Assert.Equal(10, await OnHandAsync(s, s.SourceWarehouse));
        Assert.Equal(0, await InTransitAsync(s));

        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

        // In transit: gone from the source, not yet at the destination, but accounted for.
        Assert.Equal(4, await OnHandAsync(s, s.SourceWarehouse));
        Assert.Equal(0, await OnHandAsync(s, s.DestWarehouse));
        Assert.Equal(6, await InTransitAsync(s));
        Assert.Equal("InTransit", (await GetAsync(id)).GetProperty("status").GetString());

        await JsonAsync(await ReceiveAsync(id, s.DestToken, (null, 6)), HttpStatusCode.OK);

        Assert.Equal(4, await OnHandAsync(s, s.SourceWarehouse));
        Assert.Equal(6, await OnHandAsync(s, s.DestWarehouse));
        Assert.Equal(0, await InTransitAsync(s));
        Assert.Equal("Received", (await GetAsync(id)).GetProperty("status").GetString());

        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?referenceId={id}&productId={s.ProductId}"), HttpStatusCode.OK);
        var types = movements.EnumerateArray().Select(m => m.GetProperty("movementType").GetString()).Order().ToList();
        Assert.Equal(["TransferIn", "TransferOut"], types);
    }

    [Fact]
    public async Task Dispatch_WithoutEnoughStock_MovesNothingAndStaysDraft()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 5);
        var id = await DraftAsync(s, new Line(3), new Line(3));   // 6 asked, 5 on hand

        Assert.Contains("Insufficient stock", await ErrorTextAsync(await DispatchAsync(id, s.SourceToken)));

        Assert.Equal(5, await OnHandAsync(s, s.SourceWarehouse));
        Assert.Equal("Draft", (await GetAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task OnlyADraftCanBeDispatched_AndOnlyOnce()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(4));

        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);
        Assert.Contains("Only a Draft", await ErrorTextAsync(await DispatchAsync(id, s.SourceToken)));
        Assert.Equal(6, await OnHandAsync(s, s.SourceWarehouse));
    }

    // ---- partial receipts and closing ----

    [Fact]
    public async Task PartialReceipts_ThenCloseShort_WritesTheRemainderOff()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(10));
        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

        await JsonAsync(await ReceiveAsync(id, s.DestToken, (null, 4)), HttpStatusCode.OK);
        Assert.Equal("PartiallyReceived", (await GetAsync(id)).GetProperty("status").GetString());
        Assert.Equal(4, await OnHandAsync(s, s.DestWarehouse));
        Assert.Equal(6, await InTransitAsync(s));

        // Cannot take in more than is outstanding.
        Assert.Contains("only 6 is outstanding", await ErrorTextAsync(await ReceiveAsync(id, s.DestToken, (null, 7))));

        await JsonAsync(await ReceiveAsync(id, s.DestToken, (null, 3)), HttpStatusCode.OK);
        Assert.Equal(3, await InTransitAsync(s));

        // The last 3 are not coming: closing writes them off.
        await JsonAsync(await CloseAsync(id, s.DestToken), HttpStatusCode.OK);
        var closed = await GetAsync(id);
        Assert.Equal("Received", closed.GetProperty("status").GetString());
        Assert.Equal(7, await OnHandAsync(s, s.DestWarehouse));
        Assert.Equal(0, await OnHandAsync(s, s.SourceWarehouse));
        Assert.Equal(0, await InTransitAsync(s));

        // The shortfall stays visible as dispatched minus received.
        var allocation = Allocations(closed).Single();
        Assert.Equal(10, allocation.GetProperty("quantity").GetDecimal());
        Assert.Equal(7, allocation.GetProperty("quantityReceived").GetDecimal());

        Assert.Contains("PartiallyReceived", await ErrorTextAsync(await CloseAsync(id, s.DestToken)));   // already closed
    }

    [Fact]
    public async Task Close_RequiresAPartialReceiptFirst()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 5);
        var id = await DraftAsync(s, new Line(5));
        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

        // Nothing received yet: recall it (cancel) instead of closing.
        Assert.Contains("PartiallyReceived", await ErrorTextAsync(await CloseAsync(id, s.DestToken)));
    }

    // ---- cancelling ----

    [Fact]
    public async Task CancellingAnInTransitTransfer_PutsTheStockBackIntoTheSource()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(7));
        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);
        Assert.Equal(3, await OnHandAsync(s, s.SourceWarehouse));

        await JsonAsync(await CancelAsync(id, s.SourceToken), HttpStatusCode.OK);

        Assert.Equal(10, await OnHandAsync(s, s.SourceWarehouse));
        Assert.Equal(0, await InTransitAsync(s));
        Assert.Equal("Cancelled", (await GetAsync(id)).GetProperty("status").GetString());

        var movements = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-movements?referenceId={id}&productId={s.ProductId}"), HttpStatusCode.OK);
        Assert.Contains(movements.EnumerateArray(), m => m.GetProperty("movementType").GetString() == "TransferReversal");

        // A cancelled transfer cannot be received.
        Assert.Equal(HttpStatusCode.BadRequest, (await ReceiveAsync(id, s.DestToken, (null, 1))).StatusCode);
    }

    [Fact]
    public async Task ADraftCanBeCancelled_ButNotOnceSomethingWasReceived()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);

        var draft = await DraftAsync(s, new Line(2));
        await JsonAsync(await CancelAsync(draft, s.SourceToken), HttpStatusCode.OK);
        Assert.Contains("Draft", await ErrorTextAsync(await DispatchAsync(draft, s.SourceToken)));

        var partial = await DraftAsync(s, new Line(6));
        await JsonAsync(await DispatchAsync(partial, s.SourceToken), HttpStatusCode.OK);
        await JsonAsync(await ReceiveAsync(partial, s.DestToken, (null, 2)), HttpStatusCode.OK);

        Assert.Contains("can only be cancelled", await ErrorTextAsync(await CancelAsync(partial, s.SourceToken)));
        Assert.Equal(2, await OnHandAsync(s, s.DestWarehouse));
    }

    // ---- batches ----

    [Fact]
    public async Task Dispatch_IssuesFefoAndTheDestinationReceivesTheSameBatches()
    {
        var s = await NewScenarioAsync("BatchAndExpiry");
        await JsonAsync(await AdjustAsync(s, s.SourceWarehouse, 5, "LATE", Today.AddDays(40)), HttpStatusCode.Created);
        await JsonAsync(await AdjustAsync(s, s.SourceWarehouse, 5, "SOON", Today.AddDays(10)), HttpStatusCode.Created);
        await JsonAsync(await AdjustAsync(s, s.SourceWarehouse, 5, "OLD", Today.AddDays(-2)), HttpStatusCode.Created);

        var id = await DraftAsync(s, new Line(7));
        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

        // Earliest expiry first: all of SOON, then 2 of LATE; the expired batch is left alone.
        var source = await StockAsync(s, s.SourceWarehouse);
        Assert.Equal(3, source.Single(r => r.Batch == "LATE").Quantity);
        Assert.Equal(5, source.Single(r => r.Batch == "OLD").Quantity);
        Assert.DoesNotContain(source, r => r.Batch == "SOON");

        await JsonAsync(await ReceiveAsync(id, s.DestToken, ("SOON", 5), ("LATE", 2)), HttpStatusCode.OK);

        var dest = await StockAsync(s, s.DestWarehouse);
        Assert.Equal(5, dest.Single(r => r.Batch == "SOON").Quantity);
        Assert.Equal(2, dest.Single(r => r.Batch == "LATE").Quantity);
    }

    [Fact]
    public async Task APinnedBatch_CanBeMovedEvenWhenExpired()
    {
        var s = await NewScenarioAsync("BatchAndExpiry");
        await JsonAsync(await AdjustAsync(s, s.SourceWarehouse, 5, "OLD", Today.AddDays(-2)), HttpStatusCode.Created);
        await JsonAsync(await AdjustAsync(s, s.SourceWarehouse, 5, "FRESH", Today.AddDays(40)), HttpStatusCode.Created);

        // Without pinning, expired stock cannot be issued ...
        var unpinned = await DraftAsync(s, new Line(8));
        Assert.Contains("expired", await ErrorTextAsync(await DispatchAsync(unpinned, s.SourceToken)));

        // ... but it can be named explicitly, e.g. to send it to quarantine.
        var pinned = await DraftAsync(s, new Line(3, "OLD"));
        await JsonAsync(await DispatchAsync(pinned, s.SourceToken), HttpStatusCode.OK);
        Assert.Equal(2, (await StockAsync(s, s.SourceWarehouse)).Single(r => r.Batch == "OLD").Quantity);
        await JsonAsync(await ReceiveAsync(pinned, s.DestToken, ("OLD", 3)), HttpStatusCode.OK);
        Assert.Equal(3, (await StockAsync(s, s.DestWarehouse)).Single(r => r.Batch == "OLD").Quantity);
    }

    [Fact]
    public async Task PinningABatch_IsValidated()
    {
        var plain = await NewScenarioAsync();
        Assert.Contains("does not track batches", await ErrorTextAsync(
            await CreateAsync(plain, plain.SourceToken, plain.SourceWarehouse, plain.DestWarehouse, new Line(1, "B1"))));

        var tracked = await NewScenarioAsync("Batch");
        Assert.Contains("does not exist", await ErrorTextAsync(
            await CreateAsync(tracked, tracked.SourceToken, tracked.SourceWarehouse, tracked.DestWarehouse, new Line(1, "NOPE"))));

        // A pinned batch must hold enough.
        await JsonAsync(await AdjustAsync(tracked, tracked.SourceWarehouse, 2, "B1"), HttpStatusCode.Created);
        var id = await DraftAsync(tracked, new Line(5, "B1"));
        Assert.Contains("Insufficient stock in the chosen batch", await ErrorTextAsync(await DispatchAsync(id, tracked.SourceToken)));
    }

    // ---- who can do what ----

    [Fact]
    public async Task EachEndOfATransfer_ActsOnlyOnItsOwnSide()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(5));

        // The destination branch cannot dispatch; an unrelated branch cannot even see it.
        Assert.Equal(HttpStatusCode.Forbidden, (await DispatchAsync(id, s.DestToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DispatchAsync(id, s.OtherToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/inventory/transfers/{id}", token: s.OtherToken)).StatusCode);

        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

        // The source cannot receive or close; only the destination can.
        Assert.Equal(HttpStatusCode.Forbidden, (await ReceiveAsync(id, s.SourceToken, (null, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CloseAsync(id, s.SourceToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReceiveAsync(id, s.OtherToken, (null, 5))).StatusCode);

        // The destination cannot recall it.
        Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(id, s.DestToken)).StatusCode);

        await JsonAsync(await ReceiveAsync(id, s.DestToken, (null, 5)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lists_ShowATransferToBothEndsButNotToStrangers()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(3));

        async Task<List<int>> IdsAsync(string token)
        {
            var list = await JsonAsync(await SendAsync(HttpMethod.Get, "/inventory/transfers", token: token), HttpStatusCode.OK);
            return list.EnumerateArray().Select(t => t.GetProperty("stockTransferId").GetInt32()).ToList();
        }

        Assert.Contains(id, await IdsAsync(s.SourceToken));
        Assert.Contains(id, await IdsAsync(s.DestToken));
        Assert.DoesNotContain(id, await IdsAsync(s.OtherToken));

        // The branch filter matches either end.
        var byDest = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/transfers?branchId={s.DestBranch}"), HttpStatusCode.OK);
        Assert.Contains(byDest.EnumerateArray(), t => t.GetProperty("stockTransferId").GetInt32() == id);
        var bySource = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/transfers?branchId={s.SourceBranch}"), HttpStatusCode.OK);
        Assert.Contains(bySource.EnumerateArray(), t => t.GetProperty("stockTransferId").GetInt32() == id);
    }

    [Fact]
    public async Task InTransitStock_IsVisibleOnlyToTheTwoEnds()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var id = await DraftAsync(s, new Line(4));
        await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

        async Task<int> CountAsync(string token)
        {
            var rows = await JsonAsync(await SendAsync(HttpMethod.Get, $"/inventory/stock-in-transit?productId={s.ProductId}", token: token), HttpStatusCode.OK);
            return rows.GetArrayLength();
        }

        Assert.Equal(1, await CountAsync(s.SourceToken));
        Assert.Equal(1, await CountAsync(s.DestToken));
        Assert.Equal(0, await CountAsync(s.OtherToken));
    }

    [Fact]
    public async Task Creating_NeedsASourceTheCallerCanUse_ButAnyActiveDestination()
    {
        var s = await NewScenarioAsync();

        // Staff pick the destination from a lookup of every branch's warehouses (names only).
        var destinations = await JsonAsync(await SendAsync(HttpMethod.Get, "/inventory/transfer-destinations", token: s.SourceToken), HttpStatusCode.OK);
        Assert.Contains(destinations.EnumerateArray(), d => d.GetProperty("warehouseId").GetInt32() == s.DestWarehouse);
        Assert.Contains(destinations.EnumerateArray(), d => d.GetProperty("warehouseId").GetInt32() == s.OtherWarehouse);

        // ... but the source must be one of their own.
        Assert.Contains("does not exist", await ErrorTextAsync(
            await CreateAsync(s, s.SourceToken, s.OtherWarehouse, s.DestWarehouse, new Line(1))));

        // Same warehouse both ends is meaningless.
        Assert.Contains("two different warehouses", await ErrorTextAsync(
            await CreateAsync(s, s.SourceToken, s.SourceWarehouse, s.SourceWarehouse, new Line(1))));
    }

    // ---- inactive branches ----

    [Fact]
    public async Task InactiveBranch_BlocksNewTransfersButNotFinishingExistingOnes()
    {
        var s = await NewScenarioAsync();
        await SeedAsync(s, 10);
        var inFlight = await DraftAsync(s, new Line(4));
        await JsonAsync(await DispatchAsync(inFlight, s.SourceToken), HttpStatusCode.OK);

        var branch = await JsonAsync(await SendAsync(HttpMethod.Get, $"/tenancy/branches/{s.DestBranch}"), HttpStatusCode.OK);
        await JsonAsync(await SendAsync(HttpMethod.Put, $"/tenancy/branches/{s.DestBranch}", new
        {
            code = branch.GetProperty("code").GetString(),
            name = branch.GetProperty("name").GetString(),
            isActive = false
        }), HttpStatusCode.OK);

        // No new transfers to an inactive branch, and it drops off the destinations list ...
        Assert.Contains("inactive", await ErrorTextAsync(await CreateAsync(s, s.SourceToken, s.SourceWarehouse, s.DestWarehouse, new Line(1))));
        var destinations = await JsonAsync(await SendAsync(HttpMethod.Get, "/inventory/transfer-destinations", token: s.SourceToken), HttpStatusCode.OK);
        Assert.DoesNotContain(destinations.EnumerateArray(), d => d.GetProperty("warehouseId").GetInt32() == s.DestWarehouse);

        // ... but what is already on its way can still be received.
        await JsonAsync(await ReceiveAsync(inFlight, s.DestToken, (null, 4)), HttpStatusCode.OK);
        Assert.Equal(4, await OnHandAsync(s, s.DestWarehouse));
    }

    // ---- concurrency ----

    [Fact]
    public async Task ConcurrentDispatches_NeverTakeMoreThanIsInTheSource()
    {
        for (var round = 0; round < 3; round++)
        {
            var s = await NewScenarioAsync();
            await SeedAsync(s, 10);

            var ids = new List<int>();
            for (var i = 0; i < 8; i++)
            {
                ids.Add(await DraftAsync(s, new Line(3)));
            }

            var statuses = await Task.WhenAll(ids.Select(async id => (await DispatchAsync(id, s.SourceToken)).StatusCode));

            Assert.Equal(3, statuses.Count(x => x == HttpStatusCode.OK));
            Assert.Equal(1, await OnHandAsync(s, s.SourceWarehouse));
            Assert.Equal(9, await InTransitAsync(s));
        }
    }

    [Fact]
    public async Task ARecallRacingAReceive_CanOnlyHaveOneWinner()
    {
        for (var round = 0; round < 4; round++)
        {
            var s = await NewScenarioAsync();
            await SeedAsync(s, 10);
            var id = await DraftAsync(s, new Line(6));
            await JsonAsync(await DispatchAsync(id, s.SourceToken), HttpStatusCode.OK);

            // Look the allocation up first, so the two requests below hit the server
            // at the same moment instead of the receive waiting on a lookup.
            var allocationId = Allocations(await GetAsync(id)).Single().GetProperty("stockTransferAllocationId").GetInt32();
            var receiving = SendAsync(HttpMethod.Post, $"/inventory/transfers/{id}/receive",
                new { lines = new[] { new { stockTransferAllocationId = allocationId, quantity = 6m } } }, s.DestToken);
            var cancelling = CancelAsync(id, s.SourceToken);
            var results = await Task.WhenAll(receiving, cancelling);

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));

            // Either way the books balance: 10 in total across source, destination and transit,
            // and the 6 are in exactly one place.
            var source = await OnHandAsync(s, s.SourceWarehouse);
            var dest = await OnHandAsync(s, s.DestWarehouse);
            Assert.Equal(10, source + dest + await InTransitAsync(s));
            Assert.True((source == 10 && dest == 0) || (source == 4 && dest == 6));
        }
    }
}
