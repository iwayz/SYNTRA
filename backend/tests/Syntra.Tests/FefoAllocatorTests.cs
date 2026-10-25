using Syntra.Infrastructure.Inventory;

namespace Syntra.Tests;

// The allocation rule on its own, with no database: which batches a sale
// draws from, and in what order.
public class FefoAllocatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BatchStock Batch(int id, DateOnly? expiry, decimal quantity, int createdDaysAfter = 0) => new()
    {
        BatchId = id,
        ExpiryDate = expiry,
        CreatedAt = Created.AddDays(createdDaysAfter),
        Remaining = quantity
    };

    [Fact]
    public void EarliestExpiryIsIssuedFirst()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, Today.AddDays(60), 10),
            Batch(2, Today.AddDays(10), 10),
            Batch(3, Today.AddDays(30), 10)
        };

        var result = FefoAllocator.TryAllocate(15, pool, Today)!;

        Assert.Equal([(2, 10m), (3, 5m)], result.Select(a => (a.BatchId, a.Quantity)));
    }

    [Fact]
    public void BatchesWithoutExpiryAreIssuedAfterDatedOnes()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, null, 10),
            Batch(2, Today.AddDays(90), 10)
        };

        var result = FefoAllocator.TryAllocate(12, pool, Today)!;

        Assert.Equal([(2, 10m), (1, 2m)], result.Select(a => (a.BatchId, a.Quantity)));
    }

    [Fact]
    public void BatchesWithTheSameExpiryGoOldestFirst()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, Today.AddDays(20), 5, createdDaysAfter: 10),
            Batch(2, Today.AddDays(20), 5, createdDaysAfter: 1)
        };

        var result = FefoAllocator.TryAllocate(6, pool, Today)!;

        Assert.Equal([(2, 5m), (1, 1m)], result.Select(a => (a.BatchId, a.Quantity)));
    }

    [Fact]
    public void ExpiredBatchesAreNeverIssued()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, Today.AddDays(-1), 10),
            Batch(2, Today.AddDays(5), 4)
        };

        Assert.Null(FefoAllocator.TryAllocate(5, pool, Today));

        var result = FefoAllocator.TryAllocate(4, pool, Today)!;
        Assert.Equal([(2, 4m)], result.Select(a => (a.BatchId, a.Quantity)));
        Assert.Equal(10, pool.Single(b => b.BatchId == 1).Remaining);
    }

    [Fact]
    public void ABatchExpiringTodayCanStillBeSold()
    {
        var pool = new List<BatchStock> { Batch(1, Today, 5) };

        Assert.False(FefoAllocator.IsExpired(Today, Today));
        Assert.True(FefoAllocator.IsExpired(Today.AddDays(-1), Today));
        Assert.NotNull(FefoAllocator.TryAllocate(5, pool, Today));
    }

    [Fact]
    public void WhenStockIsShortNothingIsAllocatedAndThePoolIsUntouched()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, Today.AddDays(5), 3),
            Batch(2, Today.AddDays(9), 3)
        };

        Assert.Null(FefoAllocator.TryAllocate(7, pool, Today));
        Assert.Equal([3m, 3m], pool.Select(b => b.Remaining));
    }

    [Fact]
    public void SuccessiveAllocationsConsumeTheSamePool()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, Today.AddDays(5), 4),
            Batch(2, Today.AddDays(9), 4)
        };

        var first = FefoAllocator.TryAllocate(3, pool, Today)!;
        var second = FefoAllocator.TryAllocate(3, pool, Today)!;

        Assert.Equal([(1, 3m)], first.Select(a => (a.BatchId, a.Quantity)));
        Assert.Equal([(1, 1m), (2, 2m)], second.Select(a => (a.BatchId, a.Quantity)));
        Assert.Null(FefoAllocator.TryAllocate(3, pool, Today));
    }

    [Fact]
    public void SellableAndExpiredQuantitiesAreReportedSeparately()
    {
        var pool = new List<BatchStock>
        {
            Batch(1, Today.AddDays(-3), 7),
            Batch(2, Today.AddDays(3), 4),
            Batch(3, null, 2),
            Batch(4, Today.AddDays(8), 0)
        };

        Assert.Equal(6, FefoAllocator.SellableQuantity(pool, Today));
        Assert.Equal(7, FefoAllocator.ExpiredQuantity(pool, Today));
    }
}
