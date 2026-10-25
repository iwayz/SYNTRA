using Syntra.Domain.Inventory;

namespace Syntra.Domain.Sales;

public class SalesOrder
{
    public int SalesOrderId { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public DateTime OrderDate { get; set; }
    public SalesOrderStatus Status { get; set; }

    public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();
    public ICollection<SalesReturn> Returns { get; set; } = new List<SalesReturn>();
}
