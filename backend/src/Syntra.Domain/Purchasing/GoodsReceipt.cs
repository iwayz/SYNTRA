namespace Syntra.Domain.Purchasing;

// One physical delivery against a purchase order. A PO can be received in
// several of these (partial receipts); each one creates the stock movements for
// what actually arrived. Legacy "receive all" POs from before goods receipts
// existed have no GoodsReceipt row.
public class GoodsReceipt
{
    public int GoodsReceiptId { get; set; }

    public int PurchaseOrderId { get; set; }

    public DateTime ReceiptDate { get; set; }
    public string? Notes { get; set; }

    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}
