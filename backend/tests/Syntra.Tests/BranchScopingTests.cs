using System.Net;
using System.Net.Http.Json;

namespace Syntra.Tests;

// Real data in two branches of one tenant, viewed by an admin and by a staff
// user assigned to branch A only. Tests share one tenant, so every test builds
// its own branches/warehouses/users and asserts on ids, never on totals.
public class BranchScopingTests : IClassFixture<TenantIsolationFixture>
{
    private const string Password = "P@ssw0rd123!";

    private readonly TenantIsolationFixture _fixture;

    public BranchScopingTests(TenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = _fixture.CreateRequest(method, _fixture.TenantASlug, path, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _fixture.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> AsAdmin(HttpMethod method, string path, object? body = null) =>
        SendAsync(method, path, _fixture.TenantAToken, body);

    private async Task<T> CreatedAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<List<T>> ListAsync<T>(string path, string token)
    {
        var response = await SendAsync(HttpMethod.Get, path, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<T>>())!;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    // Two branches with a warehouse each, 10 units of one product in both,
    // a draft purchase order and sales order in both, and a staff user (token)
    // assigned to branch A only.
    private async Task<Scenario> BuildAsync(bool assignStaffToA = true)
    {
        var unit = await CreatedAsync<IdDto>(await AsAdmin(HttpMethod.Post, "/inventory/units-of-measure",
            new { code = Unique("u"), name = "Piece" }));
        var product = await CreatedAsync<ProductDto>(await AsAdmin(HttpMethod.Post, "/inventory/products",
            new { sku = Unique("sku"), name = "Widget", baseUnitOfMeasureId = unit.UnitOfMeasureId }));
        var customer = await CreatedAsync<CustomerDto>(await AsAdmin(HttpMethod.Post, "/sales/customers", new { name = "Cust" }));
        var supplier = await CreatedAsync<SupplierDto>(await AsAdmin(HttpMethod.Post, "/purchasing/suppliers", new { name = "Supp" }));

        var branchA = await CreatedAsync<BranchDto>(await AsAdmin(HttpMethod.Post, "/tenancy/branches", new { code = Unique("a"), name = "Branch A" }));
        var branchB = await CreatedAsync<BranchDto>(await AsAdmin(HttpMethod.Post, "/tenancy/branches", new { code = Unique("b"), name = "Branch B" }));
        var whA = await CreatedAsync<WarehouseDto>(await AsAdmin(HttpMethod.Post, "/inventory/warehouses", new { name = "WH A", branchId = branchA.BranchId }));
        var whB = await CreatedAsync<WarehouseDto>(await AsAdmin(HttpMethod.Post, "/inventory/warehouses", new { name = "WH B", branchId = branchB.BranchId }));

        foreach (var warehouse in new[] { whA, whB })
        {
            await CreatedAsync<object>(await AsAdmin(HttpMethod.Post, "/inventory/stock-adjustments",
                new { productId = product.ProductId, warehouseId = warehouse.WarehouseId, quantity = 10, notes = "seed" }));
        }

        var poA = await CreatedAsync<PurchaseOrderDto>(await AsAdmin(HttpMethod.Post, "/purchasing/purchase-orders",
            PurchaseOrderBody(supplier, whA, product, unit)));
        var poB = await CreatedAsync<PurchaseOrderDto>(await AsAdmin(HttpMethod.Post, "/purchasing/purchase-orders",
            PurchaseOrderBody(supplier, whB, product, unit)));
        var soA = await CreatedAsync<SalesOrderDto>(await AsAdmin(HttpMethod.Post, "/sales/sales-orders",
            SalesOrderBody(customer, whA, product, unit)));
        var soB = await CreatedAsync<SalesOrderDto>(await AsAdmin(HttpMethod.Post, "/sales/sales-orders",
            SalesOrderBody(customer, whB, product, unit)));

        var email = $"staff-{Guid.NewGuid():N}@isolation-test.local";
        var staff = await CreatedAsync<UserDto>(await AsAdmin(HttpMethod.Post, "/tenancy/users", new
        {
            email,
            password = Password,
            isAdmin = false,
            branchIds = assignStaffToA ? new[] { branchA.BranchId } : []
        }));

        var login = _fixture.CreateRequest(HttpMethod.Post, _fixture.TenantASlug, "/auth/login");
        login.Content = JsonContent.Create(new { email, password = Password });
        var loginResponse = await _fixture.Client.SendAsync(login);
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<TokenDto>())!.Token;

        return new Scenario(unit, product, customer, supplier, branchA, branchB, whA, whB, poA, poB, soA, soB, staff, token);
    }

    private static object PurchaseOrderBody(SupplierDto supplier, WarehouseDto warehouse, ProductDto product, IdDto unit) => new
    {
        supplierId = supplier.SupplierId,
        warehouseId = warehouse.WarehouseId,
        orderDate = DateTime.UtcNow,
        lines = new[] { new { productId = product.ProductId, unitOfMeasureId = unit.UnitOfMeasureId, quantity = 1, unitPrice = 5 } }
    };

    private static object SalesOrderBody(CustomerDto customer, WarehouseDto warehouse, ProductDto product, IdDto unit) => new
    {
        customerId = customer.CustomerId,
        warehouseId = warehouse.WarehouseId,
        orderDate = DateTime.UtcNow,
        lines = new[] { new { productId = product.ProductId, unitOfMeasureId = unit.UnitOfMeasureId, quantity = 1, unitPrice = 5 } }
    };

    [Fact]
    public async Task Staff_SeesOnlyTheirOwnBranchData()
    {
        var s = await BuildAsync();

        var branches = await ListAsync<BranchDto>("/tenancy/branches", s.Token);
        Assert.Equal([s.BranchA.BranchId], branches.Select(b => b.BranchId));

        var warehouses = await ListAsync<WarehouseDto>("/inventory/warehouses", s.Token);
        Assert.Equal([s.WhA.WarehouseId], warehouses.Select(w => w.WarehouseId));

        var movements = await ListAsync<MovementDto>("/inventory/stock-movements", s.Token);
        Assert.All(movements, m => Assert.Equal(s.WhA.WarehouseId, m.WarehouseId));
        Assert.NotEmpty(movements);

        var balances = await ListAsync<BalanceDto>("/inventory/stock-balance", s.Token);
        var balance = Assert.Single(balances, b => b.ProductId == s.Product.ProductId);
        Assert.Equal(s.WhA.WarehouseId, balance.WarehouseId);
        Assert.Equal(s.BranchA.BranchId, balance.BranchId);
        Assert.Equal("Branch A", balance.BranchName);
        Assert.Equal(10, balance.QuantityOnHand);
        Assert.DoesNotContain(balances, b => b.WarehouseId == s.WhB.WarehouseId);

        var sales = await ListAsync<SalesOrderDto>("/sales/sales-orders", s.Token);
        Assert.Equal([s.SoA.SalesOrderId], sales.Select(o => o.SalesOrderId));

        var purchases = await ListAsync<PurchaseOrderDto>("/purchasing/purchase-orders", s.Token);
        Assert.Equal([s.PoA.PurchaseOrderId], purchases.Select(o => o.PurchaseOrderId));
    }

    [Fact]
    public async Task Admin_SeesEveryBranch_AndCanNarrowWithBranchId()
    {
        var s = await BuildAsync();
        var admin = _fixture.TenantAToken;

        var warehouses = await ListAsync<WarehouseDto>("/inventory/warehouses", admin);
        Assert.Contains(warehouses, w => w.WarehouseId == s.WhA.WarehouseId);
        Assert.Contains(warehouses, w => w.WarehouseId == s.WhB.WarehouseId);

        var balances = await ListAsync<BalanceDto>("/inventory/stock-balance", admin);
        Assert.Contains(balances, b => b.WarehouseId == s.WhA.WarehouseId);
        Assert.Contains(balances, b => b.WarehouseId == s.WhB.WarehouseId);

        var onlyB = await ListAsync<BalanceDto>($"/inventory/stock-balance?branchId={s.BranchB.BranchId}", admin);
        Assert.All(onlyB, b => Assert.Equal(s.BranchB.BranchId, b.BranchId));
        Assert.Contains(onlyB, b => b.WarehouseId == s.WhB.WarehouseId);

        var salesB = await ListAsync<SalesOrderDto>($"/sales/sales-orders?branchId={s.BranchB.BranchId}", admin);
        Assert.Equal([s.SoB.SalesOrderId], salesB.Select(o => o.SalesOrderId));

        var warehousesB = await ListAsync<WarehouseDto>($"/inventory/warehouses?branchId={s.BranchB.BranchId}", admin);
        Assert.Equal([s.WhB.WarehouseId], warehousesB.Select(w => w.WarehouseId));
    }

    [Fact]
    public async Task Staff_AskingForAnotherBranchViaFilter_GetsNothing()
    {
        var s = await BuildAsync();

        Assert.Empty(await ListAsync<WarehouseDto>($"/inventory/warehouses?branchId={s.BranchB.BranchId}", s.Token));
        Assert.Empty(await ListAsync<BalanceDto>($"/inventory/stock-balance?branchId={s.BranchB.BranchId}", s.Token));
        Assert.Empty(await ListAsync<SalesOrderDto>($"/sales/sales-orders?branchId={s.BranchB.BranchId}", s.Token));
    }

    [Fact]
    public async Task Staff_GetsNotFoundForOtherBranchResourcesById()
    {
        var s = await BuildAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/tenancy/branches/{s.BranchB.BranchId}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/inventory/warehouses/{s.WhB.WarehouseId}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{s.SoB.SalesOrderId}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{s.SoB.SalesOrderId}/confirm", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{s.SoB.SalesOrderId}/cancel", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/purchasing/purchase-orders/{s.PoB.PurchaseOrderId}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{s.PoB.PurchaseOrderId}/confirm", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{s.PoB.PurchaseOrderId}/receive", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{s.PoB.PurchaseOrderId}/cancel", s.Token)).StatusCode);

        // Their own branch's order is reachable.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{s.SoA.SalesOrderId}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{s.SoA.SalesOrderId}/confirm", s.Token)).StatusCode);
    }

    [Fact]
    public async Task Staff_CannotCreateActivityInAnotherBranch()
    {
        var s = await BuildAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/sales/sales-orders", s.Token,
            SalesOrderBody(s.Customer, s.WhB, s.Product, s.Unit))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", s.Token,
            PurchaseOrderBody(s.Supplier, s.WhB, s.Product, s.Unit))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", s.Token,
            new { productId = s.Product.ProductId, warehouseId = s.WhB.WarehouseId, quantity = 1, notes = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, "/sales/sales-orders", s.Token,
            SalesOrderBody(s.Customer, s.WhA, s.Product, s.Unit))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, "/purchasing/purchase-orders", s.Token,
            PurchaseOrderBody(s.Supplier, s.WhA, s.Product, s.Unit))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, "/inventory/stock-adjustments", s.Token,
            new { productId = s.Product.ProductId, warehouseId = s.WhA.WarehouseId, quantity = 1, notes = "x" })).StatusCode);
    }

    [Fact]
    public async Task Staff_CannotCreateWarehouses()
    {
        var s = await BuildAsync();

        var response = await SendAsync(HttpMethod.Post, "/inventory/warehouses", s.Token,
            new { name = "Sneaky", branchId = s.BranchA.BranchId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffWithNoBranches_SeesNothing()
    {
        var s = await BuildAsync(assignStaffToA: false);

        Assert.Empty(await ListAsync<BranchDto>("/tenancy/branches", s.Token));
        Assert.Empty(await ListAsync<WarehouseDto>("/inventory/warehouses", s.Token));
        Assert.Empty(await ListAsync<MovementDto>("/inventory/stock-movements", s.Token));
        Assert.Empty(await ListAsync<BalanceDto>("/inventory/stock-balance", s.Token));
        Assert.Empty(await ListAsync<SalesOrderDto>("/sales/sales-orders", s.Token));
        Assert.Empty(await ListAsync<PurchaseOrderDto>("/purchasing/purchase-orders", s.Token));
    }

    [Fact]
    public async Task RevokingABranch_TakesEffectOnTheSameToken()
    {
        var s = await BuildAsync();
        Assert.Single(await ListAsync<WarehouseDto>("/inventory/warehouses", s.Token));

        var revoke = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{s.Staff.UserId}/branches", new { branchIds = new[] { s.BranchB.BranchId } });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var warehouses = await ListAsync<WarehouseDto>("/inventory/warehouses", s.Token);
        Assert.Equal([s.WhB.WarehouseId], warehouses.Select(w => w.WarehouseId));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/sales/sales-orders/{s.SoA.SalesOrderId}", s.Token)).StatusCode);
    }

    [Fact]
    public async Task InactiveBranch_BlocksNewActivityButNotClosingOutExistingOrders()
    {
        var s = await BuildAsync();

        var deactivate = await AsAdmin(HttpMethod.Put, $"/tenancy/branches/{s.BranchA.BranchId}",
            new { code = s.BranchA.Code, name = s.BranchA.Name, isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // New activity in the inactive branch is refused, even for an admin.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/sales/sales-orders", s.Token,
            SalesOrderBody(s.Customer, s.WhA, s.Product, s.Unit))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AsAdmin(HttpMethod.Post, "/purchasing/purchase-orders",
            PurchaseOrderBody(s.Supplier, s.WhA, s.Product, s.Unit))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AsAdmin(HttpMethod.Post, "/inventory/stock-adjustments",
            new { productId = s.Product.ProductId, warehouseId = s.WhA.WarehouseId, quantity = 1, notes = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AsAdmin(HttpMethod.Post, "/inventory/warehouses",
            new { name = "Late", branchId = s.BranchA.BranchId })).StatusCode);

        // Existing orders can still be completed or cancelled, and stay visible.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/sales/sales-orders/{s.SoA.SalesOrderId}/confirm", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/purchasing/purchase-orders/{s.PoA.PurchaseOrderId}/cancel", s.Token)).StatusCode);
        Assert.Contains(await ListAsync<SalesOrderDto>("/sales/sales-orders", s.Token), o => o.SalesOrderId == s.SoA.SalesOrderId);
    }

    private record IdDto(int UnitOfMeasureId);
    private record ProductDto(int ProductId);
    private record CustomerDto(int CustomerId);
    private record SupplierDto(int SupplierId);
    private record BranchDto(int BranchId, string Code, string Name);
    private record WarehouseDto(int WarehouseId);
    private record MovementDto(int WarehouseId);
    private record BalanceDto(int ProductId, int WarehouseId, int BranchId, string BranchName, decimal QuantityOnHand);
    private record SalesOrderDto(int SalesOrderId);
    private record PurchaseOrderDto(int PurchaseOrderId);
    private record UserDto(int UserId);
    private record TokenDto(string Token);

    private record Scenario(
        IdDto Unit,
        ProductDto Product,
        CustomerDto Customer,
        SupplierDto Supplier,
        BranchDto BranchA,
        BranchDto BranchB,
        WarehouseDto WhA,
        WarehouseDto WhB,
        PurchaseOrderDto PoA,
        PurchaseOrderDto PoB,
        SalesOrderDto SoA,
        SalesOrderDto SoB,
        UserDto Staff,
        string Token);
}
