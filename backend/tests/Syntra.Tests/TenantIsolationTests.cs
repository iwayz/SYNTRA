using System.Net;

namespace Syntra.Tests;

public class TenantIsolationTests : IClassFixture<TenantIsolationFixture>
{
    private readonly TenantIsolationFixture _fixture;

    public TenantIsolationTests(TenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ActiveTenant_ResolvesSuccessfully()
    {
        var request = _fixture.CreateRequest(HttpMethod.Get, _fixture.TenantASlug, "/auth/me", _fixture.TenantAToken);
        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSlug_ReturnsNotFound()
    {
        var request = _fixture.CreateRequest(HttpMethod.Get, _fixture.UnknownSlug, "/auth/me");
        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Cancelled")]
    [InlineData("Provisioning")]
    public async Task BlockedTenantStatus_ReturnsForbidden(string statusName)
    {
        var slug = statusName switch
        {
            "Suspended" => _fixture.SuspendedSlug,
            "Cancelled" => _fixture.CancelledSlug,
            "Provisioning" => _fixture.ProvisioningSlug,
            _ => throw new ArgumentOutOfRangeException(nameof(statusName))
        };

        var request = _fixture.CreateRequest(HttpMethod.Get, slug, "/auth/me");
        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoToken_ReturnsUnauthorized()
    {
        var request = _fixture.CreateRequest(HttpMethod.Get, _fixture.TenantASlug, "/auth/me");
        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenFromOtherTenant_IsRejected()
    {
        // Tenant A's token, presented against Tenant B's subdomain, must be blocked.
        var request = _fixture.CreateRequest(HttpMethod.Get, _fixture.TenantBSlug, "/auth/me", _fixture.TenantAToken);
        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequests_NeverCrossContaminateTenantContext()
    {
        // Guards against a Scoped-to-Singleton DI regression on
        // ICurrentTenant/TenantDbContext, which would silently leak one
        // tenant's context into another tenant's concurrent request.
        const int iterations = 20;
        var tasks = new List<Task<(string ExpectedSlug, HttpResponseMessage Response)>>();

        for (var i = 0; i < iterations; i++)
        {
            var useA = i % 2 == 0;
            var slug = useA ? _fixture.TenantASlug : _fixture.TenantBSlug;
            var token = useA ? _fixture.TenantAToken : _fixture.TenantBToken;

            var request = _fixture.CreateRequest(HttpMethod.Get, slug, "/auth/me", token);
            tasks.Add(_fixture.Client.SendAsync(request).ContinueWith(t => (slug, t.Result)));
        }

        var results = await Task.WhenAll(tasks);

        foreach (var (expectedSlug, response) in results)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains($"\"tenantSlug\":\"{expectedSlug}\"", body);
        }
    }
}
