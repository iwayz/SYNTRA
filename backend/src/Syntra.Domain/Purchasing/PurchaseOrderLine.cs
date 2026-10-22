using Syntra.Domain.Inventory;

namespace Syntra.Domain.Purchasing;

// Quantity and UnitOfMeasure are the unit the line was ordered in - not
// necessarily the product's base unit. Converted to base unit only when the
// order is received (see PurchaseOrdersController.Receive).
public class PurchaseOrderLine
{
    public int PurchaseOrderLineId { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = default!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure UnitOfMeasure { get; set; } = default!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
