namespace Syntra.Domain.Inventory;

// A lot of one product. Tenant-wide, not per warehouse: the same batch can sit
// in several warehouses (and, from transfers on, move between them). The batch
// number is unique per product, case-insensitively.
public class ProductBatch
{
    // Name of the batch that holds a product's pre-existing stock when batch
    // tracking is first switched on for it.
    public const string OpeningBatchNumber = "OPENING";

    public int ProductBatchId { get; set; }

    public int ProductId { get; set; }

    public string BatchNumber { get; set; } = default!;

    // Only meaningful for products tracked as BatchAndExpiry. A batch past this
    // date (before today, UTC) can no longer be sold.
    public DateOnly? ExpiryDate { get; set; }

    public DateTime CreatedAt { get; set; }
}
