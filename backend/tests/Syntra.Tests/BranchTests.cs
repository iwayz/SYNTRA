using System.Net;
using System.Net.Http.Json;

namespace Syntra.Tests;

public class BranchTests : IClassFixture<TenantIsolationFixture>
{
    private readonly TenantIsolationFixture _fixture;

    public BranchTests(TenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = _fixture.CreateRequest(method, _fixture.TenantASlug, path, _fixture.TenantAToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task NewTenant_HasSeededMainBranch()
    {
        var response = await _fixture.Client.SendAsync(Request(HttpMethod.Get, "/tenancy/branches"));
        response.EnsureSuccessStatusCode();

        var branches = await response.Content.ReadFromJsonAsync<List<BranchDto>>();

        var main = Assert.Single(branches!, b => b.BranchId == 1);
        Assert.Equal("MAIN", main.Code);
        Assert.True(main.IsActive);
    }

    [Fact]
    public async Task DuplicateBranchCode_ReturnsConflict()
    {
        var first = await _fixture.Client.SendAsync(Request(HttpMethod.Post, "/tenancy/branches", new { code = "dup", name = "First" }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _fixture.Client.SendAsync(Request(HttpMethod.Post, "/tenancy/branches", new { code = "DUP", name = "Second" }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Warehouse_RequiresExistingActiveBranch()
    {
        var missing = await _fixture.Client.SendAsync(Request(HttpMethod.Post, "/inventory/warehouses", new { name = "W1", branchId = 99999 }));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var created = await _fixture.Client.SendAsync(Request(HttpMethod.Post, "/tenancy/branches", new { code = "closed", name = "Closed" }));
        var branch = await created.Content.ReadFromJsonAsync<BranchDto>();

        var deactivate = await _fixture.Client.SendAsync(Request(
            HttpMethod.Put, $"/tenancy/branches/{branch!.BranchId}", new { code = "closed", name = "Closed", isActive = false }));
        deactivate.EnsureSuccessStatusCode();

        var inactive = await _fixture.Client.SendAsync(Request(HttpMethod.Post, "/inventory/warehouses", new { name = "W2", branchId = branch.BranchId }));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        var ok = await _fixture.Client.SendAsync(Request(HttpMethod.Post, "/inventory/warehouses", new { name = "W3", branchId = 1 }));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    private record BranchDto(int BranchId, string Code, string Name, bool IsActive);
}
