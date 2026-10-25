using Syntra.Domain.Inventory;

namespace Syntra.Domain.Sales;

// Goods a customer sent back against a confirmed sales order. Recorded and applied
// in one step; a mistake is fixed with a stock adjustment, not by editing the return.
// Quantities only - refunds and credit notes belong to invoicing, not here.
public class SalesReturn
{
    public int SalesReturnId { get; set; }

    public int SalesOrderId { get; set; }

    public DateTime ReturnDate { get; set; }
    public string Reason { get; set; } = default!;

    public ICollection<SalesReturnLine> Lines { get; set; } = new List<SalesReturnLine>();
}

// Quantity is in the order line's own unit; BaseQuantity is the same amount in the
// product's base unit (what stock, and the per-batch limits, are measured in).
public class SalesReturnLine
{
    public int SalesReturnLineId { get; set; }

    public int SalesReturnId { get; set; }

    public int SalesOrderLineId { get; set; }

    public int ProductId { get; set; }

    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }

    // The batch the goods go back to, for batch-tracked products.
    public int? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }

    public ReturnDisposition Disposition { get; set; }

    // The stock movement a Restock created; null for a WriteOff (nothing enters stock).
    public StockMovement? StockMovement { get; set; }
}
