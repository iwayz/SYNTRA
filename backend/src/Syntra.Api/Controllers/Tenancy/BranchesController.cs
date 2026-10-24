using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Tenancy;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Tenancy;

public record CreateBranchRequest(string Code, string Name);

public record UpdateBranchRequest(string Code, string Name, bool IsActive);

public class CreateBranchRequestValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class UpdateBranchRequestValidator : AbstractValidator<UpdateBranchRequest>
{
    public UpdateBranchRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

[ApiController]
[Authorize]
[Route("tenancy/branches")]
public class BranchesController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;

    public BranchesController(TenantDbContext tenantDb, ICurrentUserAccess userAccess)
    {
        _tenantDb = tenantDb;
        _userAccess = userAccess;
    }

    // Non-admins only see the branches they are assigned to.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Branch>>> List()
    {
        var access = await _userAccess.GetAsync();
        var branches = await _tenantDb.Branches.AsNoTracking().VisibleTo(access).OrderBy(b => b.Name).ToListAsync();
        return Ok(branches);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Branch>> Get(int id)
    {
        var access = await _userAccess.GetAsync();
        var branch = await _tenantDb.Branches.AsNoTracking().VisibleTo(access).FirstOrDefaultAsync(b => b.BranchId == id);
        return branch is null ? NotFound() : Ok(branch);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    public async Task<ActionResult<Branch>> Create(CreateBranchRequest request)
    {
        var branch = new Branch { Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim() };

        _tenantDb.Branches.Add(branch);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = branch.BranchId }, branch);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    public async Task<ActionResult<Branch>> Update(int id, UpdateBranchRequest request)
    {
        var branch = await _tenantDb.Branches.FirstOrDefaultAsync(b => b.BranchId == id);
        if (branch is null)
        {
            return NotFound();
        }

        branch.Code = request.Code.Trim().ToUpperInvariant();
        branch.Name = request.Name.Trim();
        branch.IsActive = request.IsActive;
        await _tenantDb.SaveChangesAsync();

        return Ok(branch);
    }
}
