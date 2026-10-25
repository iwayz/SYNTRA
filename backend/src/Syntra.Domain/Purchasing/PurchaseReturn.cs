using Syntra.Domain.Inventory;

namespace Syntra.Domain.Purchasing;

// Goods sent back to the supplier against a received purchase order. Recorded and
// applied in one step. Quantities only - no credit notes.
public class PurchaseReturn
{
    public int PurchaseReturnId { get; set; }

    public int PurchaseOrderId { get; set; }

    public DateTime ReturnDate { get; set; }
    public string Reason { get; set; } = default!;

    public ICollection<PurchaseReturnLine> Lines { get; set; } = new List<PurchaseReturnLine>();
}

// Quantity is in the PO line's own unit; BaseQuantity is what actually left stock.
public class PurchaseReturnLine
{
    public int PurchaseReturnLineId { get; set; }

    public int PurchaseReturnId { get; set; }

    public int PurchaseOrderLineId { get; set; }

    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }

    // The batch the goods leave from, for batch-tracked products.
    public int? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }

    public StockMovement StockMovement { get; set; } = default!;
}
