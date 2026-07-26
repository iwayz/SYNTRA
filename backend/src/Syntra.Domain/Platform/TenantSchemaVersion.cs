namespace Syntra.Domain.Platform;

public class TenantSchemaVersion
{
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = default!;
    public string CurrentMigration { get; set; } = default!;
    public DateTime LastAttemptAt { get; set; }
    public MigrationRunStatus LastStatus { get; set; }
    public string? LastError { get; set; }
}
