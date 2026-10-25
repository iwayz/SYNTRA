namespace Syntra.Domain.Inventory;

// Quantity is always signed, in the product's base unit: positive = stock in,
// negative = stock out. MovementType is the reporting classification and
// also determines what ReferenceId points to (Purchase -> PurchaseOrderId,
// Sale -> SalesOrderId, ManualAdjustment -> null).
public class StockMovement
{
    public int StockMovementId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public decimal Quantity { get; set; }
    public MovementType MovementType { get; set; }
    public int? ReferenceId { get; set; }

    // Set for movements of batch-tracked products, null otherwise.
    public int? BatchId { get; set; }
    public ProductBatch? Batch { get; set; }

    public DateTime OccurredAt { get; set; }
    public string? Notes { get; set; }
}
