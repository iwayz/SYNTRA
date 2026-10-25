namespace Syntra.Domain.Inventory;

// Moves stock from one warehouse to another, possibly in a different branch.
// Stock leaves the source at dispatch and arrives at the destination when it is
// received; in between it is "in transit" - not in any warehouse, derived from
// what was dispatched minus what was received.
public class StockTransfer
{
    public int StockTransferId { get; set; }

    public int FromWarehouseId { get; set; }
    public Warehouse FromWarehouse { get; set; } = default!;

    public int ToWarehouseId { get; set; }
    public Warehouse ToWarehouse { get; set; } = default!;

    public StockTransferStatus Status { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }

    // When it became Received (fully received, or closed short).
    public DateTime? CompletedAt { get; set; }

    public ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
}

// Quantity is always in the product's base unit (transfers move physical stock;
// unit conversion is not offered here).
public class StockTransferLine
{
    public int StockTransferLineId { get; set; }

    public int StockTransferId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public decimal Quantity { get; set; }

    // Optionally pins the batch to take (batch-tracked products). Without it the
    // dispatch issues first-expiry-first-out and skips expired batches.
    public int? PinnedBatchId { get; set; }
    public ProductBatch? PinnedBatch { get; set; }

    public ICollection<StockTransferAllocation> Allocations { get; set; } = new List<StockTransferAllocation>();
}

// Exactly what left the source at dispatch (one row per batch, or a single row
// with no batch for an untracked product). Receiving works from these rows so
// the destination gets the same batches; outstanding = Quantity - QuantityReceived.
public class StockTransferAllocation
{
    public int StockTransferAllocationId { get; set; }

    public int StockTransferLineId { get; set; }

    public int? ProductBatchId { get; set; }
    public ProductBatch? Batch { get; set; }

    public decimal Quantity { get; set; }
    public decimal QuantityReceived { get; set; }

    public StockMovement DispatchMovement { get; set; } = default!;
}
