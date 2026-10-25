namespace Syntra.Infrastructure.Inventory;

// One batch's stock in one warehouse, as a mutable working copy: allocating
// from it lowers Remaining, so several sales-order lines for the same product
// draw from the same pool and can't both claim the same units.
public class BatchStock
{
    public int BatchId { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public decimal Remaining { get; set; }
}

public record BatchAllocation(int BatchId, decimal Quantity);

// First-expiry-first-out. Pure logic with no database access so it can be
// tested directly.
public static class FefoAllocator
{
    // A batch is expired once its expiry date is before today; on the expiry
    // date itself it can still be sold.
    public static bool IsExpired(DateOnly? expiryDate, DateOnly today) => expiryDate is { } date && date < today;

    public static decimal SellableQuantity(IEnumerable<BatchStock> pool, DateOnly today) =>
        pool.Where(b => b.Remaining > 0 && !IsExpired(b.ExpiryDate, today)).Sum(b => b.Remaining);

    public static decimal ExpiredQuantity(IEnumerable<BatchStock> pool, DateOnly today) =>
        pool.Where(b => b.Remaining > 0 && IsExpired(b.ExpiryDate, today)).Sum(b => b.Remaining);

    // Takes `quantity` from the sellable batches in issue order - earliest expiry
    // first, batches without an expiry after those, then oldest batch first -
    // and lowers the pool accordingly. Returns the allocations, or null (and
    // leaves the pool untouched) when the sellable stock can't cover it.
    public static IReadOnlyList<BatchAllocation>? TryAllocate(decimal quantity, IList<BatchStock> pool, DateOnly today)
    {
        if (SellableQuantity(pool, today) < quantity)
        {
            return null;
        }

        var allocations = new List<BatchAllocation>();
        var needed = quantity;

        var ordered = pool
            .Where(b => b.Remaining > 0 && !IsExpired(b.ExpiryDate, today))
            .OrderBy(b => b.ExpiryDate is null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAt)
            .ThenBy(b => b.BatchId);

        foreach (var batch in ordered)
        {
            if (needed <= 0)
            {
                break;
            }

            var take = Math.Min(batch.Remaining, needed);
            batch.Remaining -= take;
            needed -= take;
            allocations.Add(new BatchAllocation(batch.BatchId, take));
        }

        return allocations;
    }
}
