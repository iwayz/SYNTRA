using Syntra.Domain.Inventory;

namespace Syntra.Domain.Sales;

// Quantity and UnitOfMeasure are the unit the line was sold in - not
// necessarily the product's base unit. Converted to base unit only when the
// order is confirmed (see SalesOrdersController.Confirm).
public class SalesOrderLine
{
    public int SalesOrderLineId { get; set; }

    public int SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = default!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure UnitOfMeasure { get; set; } = default!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    // Running total returned by customers, in this line's own unit. Kept in step
    // with the return documents inside the same stock transaction.
    public decimal QuantityReturned { get; set; }
}
