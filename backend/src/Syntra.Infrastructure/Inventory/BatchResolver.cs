using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Infrastructure.Inventory;

// Turns the batch details on a stock-in or stock-out request (batch number,
// expiry) into a ProductBatch, enforcing the product's tracking mode. Shared by
// goods receipts and manual adjustments so both apply the same rules.
public interface IBatchResolver
{
    // Stock coming in: finds the batch by number, or creates it (not yet saved).
    Task<(ProductBatch? Batch, string? Error)> ResolveForStockInAsync(
        Product product, string? batchNumber, DateOnly? expiryDate);

    // Stock going out: the batch must already exist.
    Task<(ProductBatch? Batch, string? Error)> FindForStockOutAsync(Product product, string? batchNumber);
}

public class BatchResolver : IBatchResolver
{
    public const int MaxBatchNumberLength = 50;

    private readonly TenantDbContext _tenantDb;

    public BatchResolver(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    public async Task<(ProductBatch? Batch, string? Error)> ResolveForStockInAsync(
        Product product, string? batchNumber, DateOnly? expiryDate)
    {
        var number = batchNumber?.Trim();

        if (product.BatchTracking == BatchTracking.None)
        {
            return string.IsNullOrEmpty(number) && expiryDate is null
                ? (null, null)
                : (null, $"Product '{product.Name}' does not track batches; leave the batch and expiry fields empty.");
        }

        if (string.IsNullOrEmpty(number))
        {
            return (null, $"Product '{product.Name}' is batch-tracked; a batch number is required.");
        }

        if (number.Length > MaxBatchNumberLength)
        {
            return (null, $"Batch numbers can be at most {MaxBatchNumberLength} characters.");
        }

        if (product.BatchTracking == BatchTracking.Batch && expiryDate is not null)
        {
            return (null, $"Product '{product.Name}' is tracked by batch only; expiry dates are not used.");
        }

        var existing = await FindAsync(product.ProductId, number);

        if (existing is not null)
        {
            if (expiryDate is not null && existing.ExpiryDate != expiryDate)
            {
                var current = existing.ExpiryDate?.ToString("yyyy-MM-dd") ?? "none";
                return (null, $"Batch '{existing.BatchNumber}' already exists with expiry {current}; " +
                              $"it cannot be received with expiry {expiryDate:yyyy-MM-dd}.");
            }

            return (existing, null);
        }

        if (product.BatchTracking == BatchTracking.BatchAndExpiry && expiryDate is null)
        {
            return (null, $"Product '{product.Name}' needs an expiry date for new batch '{number}'.");
        }

        var batch = new ProductBatch
        {
            ProductId = product.ProductId,
            BatchNumber = number,
            ExpiryDate = expiryDate,
            CreatedAt = DateTime.UtcNow
        };

        _tenantDb.ProductBatches.Add(batch);
        return (batch, null);
    }

    public async Task<(ProductBatch? Batch, string? Error)> FindForStockOutAsync(Product product, string? batchNumber)
    {
        var number = batchNumber?.Trim();

        if (product.BatchTracking == BatchTracking.None)
        {
            return string.IsNullOrEmpty(number)
                ? (null, null)
                : (null, $"Product '{product.Name}' does not track batches; leave the batch field empty.");
        }

        if (string.IsNullOrEmpty(number))
        {
            return (null, $"Product '{product.Name}' is batch-tracked; name the batch to take stock from.");
        }

        var existing = await FindAsync(product.ProductId, number);

        return existing is null
            ? (null, $"Batch '{number}' does not exist for product '{product.Name}'.")
            : (existing, null);
    }

    // Looks at batches added earlier in this request first (they are not saved
    // yet, e.g. two receipt lines naming the same new batch), then the database.
    private async Task<ProductBatch?> FindAsync(int productId, string number)
    {
        var local = _tenantDb.ProductBatches.Local.FirstOrDefault(b =>
            b.ProductId == productId && string.Equals(b.BatchNumber, number, StringComparison.OrdinalIgnoreCase));

        return local ?? await _tenantDb.ProductBatches
            .FirstOrDefaultAsync(b => b.ProductId == productId && b.BatchNumber == number);
    }
}
