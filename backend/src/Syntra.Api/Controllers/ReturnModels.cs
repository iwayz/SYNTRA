using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers;

// What can still be sent back on one order line (all quantities in the line's own
// unit). For batch-tracked products, Batches says which batches the goods can go
// back to / leave from, and how much each can still take.
public record ReturnableBatch(int BatchId, string BatchNumber, DateOnly? ExpiryDate, decimal Returnable);

public record ReturnableLine(
    int LineId,
    int ProductId,
    string ProductName,
    string UnitCode,
    decimal Ordered,
    decimal Returned,
    decimal Returnable,
    bool BatchTracked,
    IReadOnlyList<ReturnableBatch> Batches);

// How many base units one unit of `unitOfMeasureId` is for this product.
internal static class UnitFactors
{
    public static async Task<decimal> ToBaseAsync(TenantDbContext db, Product product, int unitOfMeasureId)
    {
        if (unitOfMeasureId == product.BaseUnitOfMeasureId)
        {
            return 1m;
        }

        var conversion = await db.ProductUnitConversions.AsNoTracking()
            .FirstAsync(c => c.ProductId == product.ProductId && c.UnitOfMeasureId == unitOfMeasureId);

        return conversion.ConversionFactor;
    }
}
