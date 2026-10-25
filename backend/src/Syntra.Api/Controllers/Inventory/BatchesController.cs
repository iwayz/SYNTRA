using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record UpdateBatchExpiryRequest(DateOnly ExpiryDate);

[ApiController]
[Authorize]
[Route("inventory/batches")]
public class BatchesController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public BatchesController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    // Batches are tenant-wide master data like products, so any signed-in user
    // can list them (what stock each holds is in the branch-scoped stock balance).
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductBatch>>> List([FromQuery] int? productId)
    {
        var query = _tenantDb.ProductBatches.AsNoTracking().AsQueryable();

        if (productId is not null)
        {
            query = query.Where(b => b.ProductId == productId);
        }

        var batches = await query
            .OrderBy(b => b.ProductId)
            .ThenBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync();

        return Ok(batches);
    }

    // Fixes a mistyped expiry. Only the expiry can change - never the batch
    // number, which movements and receipts refer to by name.
    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    public async Task<ActionResult<ProductBatch>> UpdateExpiry(int id, UpdateBatchExpiryRequest request)
    {
        var batch = await _tenantDb.ProductBatches.FirstOrDefaultAsync(b => b.ProductBatchId == id);
        if (batch is null)
        {
            return NotFound();
        }

        var product = await _tenantDb.Products.AsNoTracking().FirstAsync(p => p.ProductId == batch.ProductId);
        if (product.BatchTracking != BatchTracking.BatchAndExpiry)
        {
            return ValidationProblem($"Product '{product.Name}' does not track expiry dates.");
        }

        batch.ExpiryDate = request.ExpiryDate;
        await _tenantDb.SaveChangesAsync();

        return Ok(batch);
    }
}
