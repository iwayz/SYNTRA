using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
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
    private readonly ICurrentUserAccess _userAccess;

    public WarehousesController(TenantDbContext tenantDb, ICurrentUserAccess userAccess)
    {
        _tenantDb = tenantDb;
        _userAccess = userAccess;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Warehouse>>> List([FromQuery] int? branchId)
    {
        var access = await _userAccess.GetAsync();

        var warehouses = await _tenantDb.Warehouses.AsNoTracking()
            .VisibleTo(access, branchId)
            .Include(w => w.Branch)
            .ToListAsync();

        return Ok(warehouses);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Warehouse>> Get(int id)
    {
        var access = await _userAccess.GetAsync();

        var warehouse = await _tenantDb.Warehouses.AsNoTracking()
            .VisibleTo(access)
            .Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == id);

        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    // Warehouses are structural setup (like branches), so admin only.
    [HttpPost]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
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
