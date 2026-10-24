namespace Syntra.Domain.Tenancy;

// A physical or logical business location (store, outlet, depot) within one
// tenant. A branch owns one or more warehouses; stock movements and orders
// reach a branch only through their warehouse.
public class Branch
{
    // The branch every tenant starts with. Seeded by the migration that
    // introduced branches (HasData), so existing and newly provisioned tenant
    // databases both get the same row.
    public const int MainBranchId = 1;
    public const string MainBranchCode = "MAIN";
    public const string MainBranchName = "Main";

    public int BranchId { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}
