using System.Net;
using System.Net.Http.Json;

namespace Syntra.Tests;

// Two ephemeral tenants (from TenantIsolationFixture) filled with parallel,
// recognisable business data. Both are seeded by the exact same sequence of
// calls, so their numeric ids collide on purpose - that makes "A's id on B's
// subdomain" a meaningful test, and the shared SKU / unit / branch code prove
// uniqueness is per tenant. What differs is a per-tenant marker inside every
// name, plus the stock quantity, so any leak is detectable by content.
public class TwoTenantDataFixture : IAsyncLifetime
{
    public const string SharedSku = "SKU-SHARED";
    public const string SharedUnitCode = "PCS";
    public const string SharedBranchCode = "BR-SHARED";
    public const string SharedUserEmail = "shared@isolation-test.local";

    private readonly string _runId = Guid.NewGuid().ToString("N")[..6];

    public TenantIsolationFixture Base { get; } = new();

    public TenantData A { get; private set; } = default!;
    public TenantData B { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Base.InitializeAsync();

        A = await SeedAsync(Base.TenantASlug, Base.TenantAToken, $"MKA{_runId}", stockQuantity: 10, password: "P@ssw0rd-A1!");
        B = await SeedAsync(Base.TenantBSlug, Base.TenantBToken, $"MKB{_runId}", stockQuantity: 7, password: "P@ssw0rd-B2!");
    }

    public Task DisposeAsync() => Base.DisposeAsync();

    public async Task<HttpResponseMessage> SendAsync(
        TenantData tenant,
        HttpMethod method,
        string path,
        object? body = null,
        string? token = null,
        string? hostOverride = null)
    {
        var request = Base.CreateRequest(method, hostOverride ?? tenant.Slug, path, token ?? tenant.AdminToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Base.Client.SendAsync(request);
    }

    private async Task<T> CreateAsync<T>(TenantData tenant, string path, object body)
    {
        var response = await SendAsync(tenant, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<TenantData> SeedAsync(string slug, string adminToken, string marker, decimal stockQuantity, string password)
    {
        var tenant = new TenantData(slug, adminToken, marker, password);

        var unit = await CreateAsync<UnitDto>(tenant, "/inventory/units-of-measure",
            new { code = SharedUnitCode, name = $"Piece {marker}" });
        var product = await CreateAsync<ProductDto>(tenant, "/inventory/products",
            new { sku = SharedSku, name = $"Widget {marker}", baseUnitOfMeasureId = unit.UnitOfMeasureId });
        var customer = await CreateAsync<CustomerDto>(tenant, "/sales/customers", new { name = $"Customer {marker}" });
        var supplier = await CreateAsync<SupplierDto>(tenant, "/purchasing/suppliers", new { name = $"Supplier {marker}" });
        var branch = await CreateAsync<BranchDto>(tenant, "/tenancy/branches",
            new { code = SharedBranchCode, name = $"Branch {marker}" });
        var warehouse = await CreateAsync<WarehouseDto>(tenant, "/inventory/warehouses",
            new { name = $"Warehouse {marker}", branchId = branch.BranchId });

        await CreateAsync<object>(tenant, "/inventory/stock-adjustments",
            new { productId = product.ProductId, warehouseId = warehouse.WarehouseId, quantity = stockQuantity, notes = $"Opening stock {marker}" });

        var line = new[] { new { productId = product.ProductId, unitOfMeasureId = unit.UnitOfMeasureId, quantity = 1, unitPrice = 5 } };
        var purchaseOrder = await CreateAsync<PurchaseOrderDto>(tenant, "/purchasing/purchase-orders",
            new { supplierId = supplier.SupplierId, warehouseId = warehouse.WarehouseId, orderDate = DateTime.UtcNow, lines = line });
        var salesOrder = await CreateAsync<SalesOrderDto>(tenant, "/sales/sales-orders",
            new { customerId = customer.CustomerId, warehouseId = warehouse.WarehouseId, orderDate = DateTime.UtcNow, lines = line });

        // The same email in both tenants, with a different password in each.
        var sharedUser = await CreateAsync<UserDto>(tenant, "/tenancy/users",
            new { email = SharedUserEmail, password, isAdmin = false, branchIds = new[] { branch.BranchId } });
        // A user whose email carries the marker, so the users list is checkable by content.
        await CreateAsync<UserDto>(tenant, "/tenancy/users",
            new { email = $"staff-{marker.ToLowerInvariant()}@isolation-test.local", password, isAdmin = false, branchIds = new[] { branch.BranchId } });

        return tenant with
        {
            UnitId = unit.UnitOfMeasureId,
            ProductId = product.ProductId,
            CustomerId = customer.CustomerId,
            SupplierId = supplier.SupplierId,
            BranchId = branch.BranchId,
            WarehouseId = warehouse.WarehouseId,
            PurchaseOrderId = purchaseOrder.PurchaseOrderId,
            SalesOrderId = salesOrder.SalesOrderId,
            SharedUserId = sharedUser.UserId
        };
    }

    private record UnitDto(int UnitOfMeasureId);
    private record ProductDto(int ProductId);
    private record CustomerDto(int CustomerId);
    private record SupplierDto(int SupplierId);
    private record BranchDto(int BranchId);
    private record WarehouseDto(int WarehouseId);
    private record PurchaseOrderDto(int PurchaseOrderId);
    private record SalesOrderDto(int SalesOrderId);
    private record UserDto(int UserId);
}

public record TenantData(string Slug, string AdminToken, string Marker, string Password)
{
    public int UnitId { get; init; }
    public int ProductId { get; init; }
    public int CustomerId { get; init; }
    public int SupplierId { get; init; }
    public int BranchId { get; init; }
    public int WarehouseId { get; init; }
    public int PurchaseOrderId { get; init; }
    public int SalesOrderId { get; init; }
    public int SharedUserId { get; init; }
}
