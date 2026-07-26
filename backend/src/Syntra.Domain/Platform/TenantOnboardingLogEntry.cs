namespace Syntra.Domain.Platform;

public class TenantOnboardingLogEntry
{
    public long LogId { get; set; }
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = default!;
    public string Step { get; set; } = default!;
    public OnboardingStepStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}
