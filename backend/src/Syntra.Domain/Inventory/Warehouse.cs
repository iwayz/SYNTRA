using Syntra.Domain.Tenancy;

namespace Syntra.Domain.Inventory;

public class Warehouse
{
    public int WarehouseId { get; set; }
    public string Name { get; set; } = default!;
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
}
