namespace Syntra.Domain.Platform;

public class Subscription
{
    public int SubscriptionId { get; set; }
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = default!;
    public string PlanName { get; set; } = default!;
    public DateOnly StartDate { get; set; }
    public DateOnly MaintenanceRenewalDate { get; set; }
    public MaintenanceFeeStatus MaintenanceFeeStatus { get; set; }
    public string BillingContactName { get; set; } = default!;
    public string BillingContactEmail { get; set; } = default!;
}
