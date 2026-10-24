using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateWarehouseRequest(string Name, int BranchId);

public class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BranchId).GreaterThan(0);
    }
}

[ApiController]
[Authorize]
[Route("inventory/warehouses")]
public class WarehousesController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public WarehousesController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Warehouse>>> List()
    {
        var warehouses = await _tenantDb.Warehouses.AsNoTracking().Include(w => w.Branch).ToListAsync();
        return Ok(warehouses);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Warehouse>> Get(int id)
    {
        var warehouse = await _tenantDb.Warehouses.AsNoTracking().Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == id);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    [HttpPost]
    public async Task<ActionResult<Warehouse>> Create(CreateWarehouseRequest request)
    {
        var branch = await _tenantDb.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == request.BranchId);
        if (branch is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Branch does not exist.");
        }

        if (!branch.IsActive)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Branch is inactive.");
        }

        var warehouse = new Warehouse { Name = request.Name, BranchId = branch.BranchId };

        _tenantDb.Warehouses.Add(warehouse);
        await _tenantDb.SaveChangesAsync();

        warehouse.Branch = branch;

        return CreatedAtAction(nameof(Get), new { id = warehouse.WarehouseId }, warehouse);
    }
}
