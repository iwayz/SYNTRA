namespace Syntra.Domain.Platform;

public class TenantModule
{
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = default!;
    public int ModuleId { get; set; }
    public ModuleDefinition Module { get; set; } = default!;
    public bool IsEnabled { get; set; }
    public DateTime ActivatedAt { get; set; }
}
