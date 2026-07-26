namespace Syntra.Domain.TenantData;

// Temporary placeholder entity so TenantDbContext has something real to
// migrate before Phase 1 defines actual business entities (Product,
// Inventory, etc.). Remove once real tenant-side entities exist.
public class SchemaProbe
{
    public int SchemaProbeId { get; set; }
    public DateTime CreatedAt { get; set; }
}
