namespace Syntra.Domain.Inventory;

// A physical stock count (opname) of one warehouse. Creating it snapshots what
// the warehouse holds; people then enter what they actually counted, row by
// row, while trading carries on. Nothing changes in stock until an admin posts
// the count, which turns each counted row's difference into a StockCount movement.
public class StockCount
{
    public int StockCountId { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public StockCountStatus Status { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PostedAt { get; set; }

    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}

// One thing being counted: a product, or a product's batch if it is batch-tracked.
public class StockCountLine
{
    public int StockCountLineId { get; set; }

    public int StockCountId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public int? ProductBatchId { get; set; }
    public ProductBatch? Batch { get; set; }

    // What the books said when the count was created. For reference only: the
    // difference that gets posted is measured against the books at posting time.
    public decimal ExpectedQuantity { get; set; }

    // Null until someone counts the row. A row left uncounted is not touched at posting.
    public decimal? CountedQuantity { get; set; }

    // When the row was counted, taken while holding the stock lock for this
    // product and warehouse. Every stock movement is posted under the same lock,
    // so "a movement after CountedAt" reliably means the row's count is out of date.
    public DateTime? CountedAt { get; set; }

    // What the books said at the moment of the count.
    public decimal? SystemQuantityAtCount { get; set; }

    // Set when the count is posted: the difference that was applied (0 if none).
    public decimal? PostedVariance { get; set; }
    public StockMovement? AdjustmentMovement { get; set; }
}
