using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateWarehouseRequest(string Name);

public class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
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
    public async Task<IActionResult> List()
    {
        var warehouses = await _tenantDb.Warehouses.AsNoTracking().ToListAsync();
        return Ok(warehouses);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var warehouse = await _tenantDb.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.WarehouseId == id);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateWarehouseRequest request)
    {
        var warehouse = new Warehouse { Name = request.Name };

        _tenantDb.Warehouses.Add(warehouse);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = warehouse.WarehouseId }, warehouse);
    }
}
