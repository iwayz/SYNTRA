using Syntra.Domain.Platform;

namespace Syntra.Infrastructure.Platform;

// Scoped, per-request. Populated once by TenantResolutionMiddleware near the
// start of the pipeline; everything else in the app reads it via ICurrentTenant.
public class CurrentTenant : ICurrentTenant
{
    public bool IsResolved { get; private set; }
    public int TenantId { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }

    public void Set(Tenant tenant)
    {
        TenantId = tenant.TenantId;
        Slug = tenant.Slug;
        ConnectionString = tenant.ConnectionString;
        Status = tenant.Status;
        IsResolved = true;
    }
}
