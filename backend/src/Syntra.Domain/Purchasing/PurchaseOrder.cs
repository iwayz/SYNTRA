using Syntra.Domain.Inventory;

namespace Syntra.Domain.Purchasing;

public class PurchaseOrder
{
    public int PurchaseOrderId { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = default!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public DateTime OrderDate { get; set; }
    public PurchaseOrderStatus Status { get; set; }

    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
    public ICollection<PurchaseReturn> Returns { get; set; } = new List<PurchaseReturn>();
}
