namespace Syntra.Domain.Platform;

public interface ICurrentTenant
{
    bool IsResolved { get; }
    int TenantId { get; }
    string Slug { get; }
    string ConnectionString { get; }
    TenantStatus Status { get; }
}
