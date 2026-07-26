namespace Syntra.Domain.Platform;

public class Tenant
{
    public int TenantId { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string DatabaseServer { get; set; } = default!;
    public string DatabaseName { get; set; } = default!;
    public string ConnectionString { get; set; } = default!;
    public TenantStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Subscription? Subscription { get; set; }
    public ICollection<TenantModule> TenantModules { get; set; } = new List<TenantModule>();
}
