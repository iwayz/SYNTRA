using Syntra.Domain.Inventory;

namespace Syntra.Domain.Purchasing;

// Quantity is in the PO line's own unit (like PurchaseOrderLine.Quantity); the
// movement it produced holds the base-unit quantity. Notes records any
// discrepancy, e.g. damaged goods or a short delivery.
public class GoodsReceiptLine
{
    public int GoodsReceiptLineId { get; set; }

    public int GoodsReceiptId { get; set; }

    public int PurchaseOrderLineId { get; set; }

    public decimal Quantity { get; set; }
    public string? Notes { get; set; }

    // Set when the product is batch-tracked.
    public int? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }

    public StockMovement StockMovement { get; set; } = default!;
}
