using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Tests;

// Phase 0's isolation tests only ever touched /auth/me. These prove that real
// business data - products, stock, orders, users - never crosses tenants. See
// TwoTenantDataFixture for how the two tenants are seeded (colliding ids,
// shared SKU/codes/email, per-tenant markers in every name).
public class TenantBusinessDataIsolationTests : IClassFixture<TwoTenantDataFixture>
{
    private readonly TwoTenantDataFixture _fixture;

    public TenantBusinessDataIsolationTests(TwoTenantDataFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string> ListEndpoints =>
    [
        "/inventory/units-of-measure",
        "/inventory/products",
        "/inventory/warehouses",
        "/sales/customers",
        "/purchasing/suppliers",
        "/tenancy/branches",
        "/tenancy/users",
        "/inventory/stock-movements",
        "/inventory/stock-balance",
        "/purchasing/purchase-orders",
        "/sales/sales-orders"
    ];

    [Fact]
    public void Precondition_IdsCollideAcrossTenants()
    {
        // If the ids differed, the by-id tests below would pass trivially.
        Assert.Equal(_fixture.A.ProductId, _fixture.B.ProductId);
        Assert.Equal(_fixture.A.WarehouseId, _fixture.B.WarehouseId);
        Assert.Equal(_fixture.A.SalesOrderId, _fixture.B.SalesOrderId);
        Assert.Equal(_fixture.A.PurchaseOrderId, _fixture.B.PurchaseOrderId);
        Assert.Equal(_fixture.A.BranchId, _fixture.B.BranchId);
        Assert.Equal(_fixture.A.SharedUserId, _fixture.B.SharedUserId);
    }

    [Theory]
    [MemberData(nameof(ListEndpoints))]
    public async Task ListEndpoint_NeverShowsTheOtherTenantsData(string path)
    {
        foreach (var (viewer, other) in new[] { (_fixture.A, _fixture.B), (_fixture.B, _fixture.A) })
        {
            var response = await _fixture.SendAsync(viewer, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();

            // Positive control first: an empty or broken endpoint must not pass.
            Assert.Contains(viewer.Marker.ToLowerInvariant(), body.ToLowerInvariant());
            Assert.DoesNotContain(other.Marker.ToLowerInvariant(), body.ToLowerInvariant());
        }
    }

    [Theory]
    [InlineData("/inventory/products/{0}", nameof(TenantData.ProductId))]
    [InlineData("/inventory/units-of-measure/{0}", nameof(TenantData.UnitId))]
    [InlineData("/inventory/warehouses/{0}", nameof(TenantData.WarehouseId))]
    [InlineData("/sales/customers/{0}", nameof(TenantData.CustomerId))]
    [InlineData("/purchasing/suppliers/{0}", nameof(TenantData.SupplierId))]
    [InlineData("/tenancy/branches/{0}", nameof(TenantData.BranchId))]
    [InlineData("/tenancy/users/{0}", nameof(TenantData.SharedUserId))]
    [InlineData("/sales/sales-orders/{0}", nameof(TenantData.SalesOrderId))]
    [InlineData("/purchasing/purchase-orders/{0}", nameof(TenantData.PurchaseOrderId))]
    public async Task ByIdWithCollidingIds_ReturnsOnlyTheCallersOwnRecord(string pathFormat, string idProperty)
    {
        foreach (var (viewer, other) in new[] { (_fixture.A, _fixture.B), (_fixture.B, _fixture.A) })
        {
            // The other tenant's id for this resource - numerically the same as ours.
            var otherId = (int)typeof(TenantData).GetProperty(idProperty)!.GetValue(other)!;

            var response = await _fixture.SendAsync(viewer, HttpMethod.Get, string.Format(pathFormat, otherId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();

            Assert.DoesNotContain(other.Marker.ToLowerInvariant(), body);
            // Users: the shared email has no marker, so only check "not the other's marker".
            if (!pathFormat.StartsWith("/tenancy/users"))
            {
                Assert.Contains(viewer.Marker.ToLowerInvariant(), body);
            }
        }
    }

    [Fact]
    public async Task IdThatOnlyExistsInOneTenant_IsNotFoundInTheOther()
    {
        var extra = await _fixture.SendAsync(_fixture.A, HttpMethod.Post, "/inventory/products",
            new { sku = $"ONLY-A-{Guid.NewGuid():N}"[..14], name = "Only in A", baseUnitOfMeasureId = _fixture.A.UnitId });
        Assert.Equal(HttpStatusCode.Created, extra.StatusCode);
        var created = (await extra.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("productId").GetInt32();
        Assert.NotEqual(_fixture.B.ProductId, created);

        var inB = await _fixture.SendAsync(_fixture.B, HttpMethod.Get, $"/inventory/products/{created}");
        Assert.Equal(HttpStatusCode.NotFound, inB.StatusCode);
    }

    [Fact]
    public async Task UniqueValues_AreScopedPerTenant()
    {
        // The shared SKU / unit code / branch code were already accepted in
        // both tenants during seeding. Show it holds for new values too, and
        // that duplicates inside one tenant are still refused.
        var sku = $"DUP-{Guid.NewGuid():N}"[..12];
        object Product(TenantData t) => new { sku, name = "Dup", baseUnitOfMeasureId = t.UnitId };

        Assert.Equal(HttpStatusCode.Created, (await _fixture.SendAsync(_fixture.A, HttpMethod.Post, "/inventory/products", Product(_fixture.A))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _fixture.SendAsync(_fixture.B, HttpMethod.Post, "/inventory/products", Product(_fixture.B))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _fixture.SendAsync(_fixture.A, HttpMethod.Post, "/inventory/products", Product(_fixture.A))).StatusCode);
    }

    [Fact]
    public async Task SameEmailInBothTenants_EachPasswordOnlyWorksOnItsOwnTenant()
    {
        async Task<HttpStatusCode> LoginAsync(TenantData tenant, string password)
        {
            var request = _fixture.Base.CreateRequest(HttpMethod.Post, tenant.Slug, "/auth/login");
            request.Content = JsonContent.Create(new { email = TwoTenantDataFixture.SharedUserEmail, password });
            return (await _fixture.Base.Client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await LoginAsync(_fixture.A, _fixture.A.Password));
        Assert.Equal(HttpStatusCode.OK, await LoginAsync(_fixture.B, _fixture.B.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(_fixture.A, _fixture.B.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(_fixture.B, _fixture.A.Password));
    }

    [Fact]
    public async Task ConfirmingASalesOrderInOneTenant_DoesNotTouchTheOther()
    {
        decimal OnHand(JsonElement list, TenantData t) =>
            list.EnumerateArray().Single(b => b.GetProperty("warehouseId").GetInt32() == t.WarehouseId
                                              && b.GetProperty("productId").GetInt32() == t.ProductId)
                .GetProperty("quantityOnHand").GetDecimal();

        async Task<JsonElement> BalanceAsync(TenantData t) =>
            await (await _fixture.SendAsync(t, HttpMethod.Get, "/inventory/stock-balance")).Content.ReadFromJsonAsync<JsonElement>();

        var beforeA = OnHand(await BalanceAsync(_fixture.A), _fixture.A);
        var beforeB = OnHand(await BalanceAsync(_fixture.B), _fixture.B);
        Assert.Equal(10, beforeA);
        Assert.Equal(7, beforeB);

        // Same order id in both tenants; confirm only B's.
        var confirm = await _fixture.SendAsync(_fixture.B, HttpMethod.Post, $"/sales/sales-orders/{_fixture.B.SalesOrderId}/confirm");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        Assert.Equal(beforeA, OnHand(await BalanceAsync(_fixture.A), _fixture.A));
        Assert.Equal(beforeB - 1, OnHand(await BalanceAsync(_fixture.B), _fixture.B));

        var orderA = await (await _fixture.SendAsync(_fixture.A, HttpMethod.Get, $"/sales/sales-orders/{_fixture.A.SalesOrderId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", orderA.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ConcurrentReadsOfBusinessData_NeverCrossTenants()
    {
        // Extends the Phase 0 concurrency test (which only read /auth/me) to a
        // data endpoint, where the per-request connection string actually matters.
        var tasks = Enumerable.Range(0, 30).Select(async i =>
        {
            var (viewer, other) = i % 2 == 0 ? (_fixture.A, _fixture.B) : (_fixture.B, _fixture.A);
            var response = await _fixture.SendAsync(viewer, HttpMethod.Get, "/inventory/products");
            return (viewer, other, response.StatusCode, Body: (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
        });

        foreach (var (viewer, other, status, body) in await Task.WhenAll(tasks))
        {
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(viewer.Marker.ToLowerInvariant(), body);
            Assert.DoesNotContain(other.Marker.ToLowerInvariant(), body);
        }
    }

    [Fact]
    public async Task ConcurrentWrites_LandInTheRightTenantDatabase()
    {
        const int perTenant = 10;
        var runTag = Guid.NewGuid().ToString("N")[..6];

        var tasks = Enumerable.Range(0, perTenant * 2).Select(async i =>
        {
            var tenant = i % 2 == 0 ? _fixture.A : _fixture.B;
            var label = i % 2 == 0 ? "A" : "B";
            var response = await _fixture.SendAsync(tenant, HttpMethod.Post, "/inventory/products",
                new { sku = $"PAR-{label}-{runTag}-{i}", name = $"Parallel {label}", baseUnitOfMeasureId = tenant.UnitId });
            return response.StatusCode;
        });

        Assert.All(await Task.WhenAll(tasks), status => Assert.Equal(HttpStatusCode.Created, status));

        async Task<List<string>> SkusAsync(TenantData t) =>
            (await (await _fixture.SendAsync(t, HttpMethod.Get, "/inventory/products")).Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray().Select(p => p.GetProperty("sku").GetString()!).Where(s => s.Contains(runTag)).ToList();

        var skusA = await SkusAsync(_fixture.A);
        var skusB = await SkusAsync(_fixture.B);

        Assert.Equal(perTenant, skusA.Count);
        Assert.All(skusA, s => Assert.StartsWith("PAR-A-", s));
        Assert.Equal(perTenant, skusB.Count);
        Assert.All(skusB, s => Assert.StartsWith("PAR-B-", s));
    }

    [Fact]
    public async Task EachTenantsDatabase_HoldsOnlyItsOwnRows()
    {
        // Straight to SQL, bypassing the API and its filters: the separation is physical.
        foreach (var (mine, other) in new[] { (_fixture.A, _fixture.B), (_fixture.B, _fixture.A) })
        {
            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(TenantIsolationFixture.ConnectionStringFor(mine.Slug))
                .Options;
            using var db = new TenantDbContext(options);

            var names = new List<string>();
            names.AddRange(await db.Products.Select(p => p.Name).ToListAsync());
            names.AddRange(await db.UnitOfMeasures.Select(u => u.Name).ToListAsync());
            names.AddRange(await db.Customers.Select(c => c.Name).ToListAsync());
            names.AddRange(await db.Suppliers.Select(s => s.Name).ToListAsync());
            names.AddRange(await db.Branches.Select(b => b.Name).ToListAsync());
            names.AddRange(await db.Warehouses.Select(w => w.Name).ToListAsync());
            names.AddRange(await db.StockMovements.Select(m => m.Notes ?? "").ToListAsync());
            names.AddRange(await db.Users.Select(u => u.Email ?? "").ToListAsync());

            Assert.Contains(names, n => n.Contains(mine.Marker, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, n => n.Contains(other.Marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ForwardedHostHeader_CannotSwitchTenant()
    {
        // Resolution reads the real Host header only. Claiming tenant A via a
        // forwarding header while talking to B must still yield B's data.
        var request = _fixture.Base.CreateRequest(HttpMethod.Get, _fixture.B.Slug, "/inventory/products", _fixture.B.AdminToken);
        request.Headers.Add("X-Forwarded-Host", $"{_fixture.A.Slug}.localhost");
        request.Headers.Add("X-Original-Host", $"{_fixture.A.Slug}.localhost");

        var response = await _fixture.Base.Client.SendAsync(request);
        var body = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_fixture.B.Marker.ToLowerInvariant(), body);
        Assert.DoesNotContain(_fixture.A.Marker.ToLowerInvariant(), body);
    }
}
