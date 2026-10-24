using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Tenancy;
using Syntra.Domain.TenantData;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Tenancy;

public record UserSummary(int UserId, string Email, bool IsActive, bool IsAdmin, IReadOnlyList<int> BranchIds);

public record CreateUserRequest(string Email, string Password, bool IsAdmin, IReadOnlyList<int> BranchIds);
public record SetUserBranchesRequest(IReadOnlyList<int> BranchIds);
public record SetUserActiveRequest(bool IsActive);
public record SetUserAdminRequest(bool IsAdmin);
public record ResetUserPasswordRequest(string NewPassword);

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.BranchIds).NotNull();
    }
}

public class SetUserBranchesRequestValidator : AbstractValidator<SetUserBranchesRequest>
{
    public SetUserBranchesRequestValidator()
    {
        RuleFor(x => x.BranchIds).NotNull();
    }
}

public class ResetUserPasswordRequestValidator : AbstractValidator<ResetUserPasswordRequest>
{
    public ResetUserPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty();
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.TenantAdmin)]
[Route("tenancy/users")]
public class UsersController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly UserManager<TenantUser> _userManager;
    private readonly ICurrentUserAccess _userAccess;

    public UsersController(TenantDbContext tenantDb, UserManager<TenantUser> userManager, ICurrentUserAccess userAccess)
    {
        _tenantDb = tenantDb;
        _userManager = userManager;
        _userAccess = userAccess;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSummary>>> List()
    {
        var users = await _tenantDb.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();

        var adminIds = (await _tenantDb.UserRoles
                .Where(ur => ur.RoleId == TenantRoles.AdminRoleId)
                .Select(ur => ur.UserId)
                .ToListAsync())
            .ToHashSet();

        var branchesByUser = (await _tenantDb.UserBranches.AsNoTracking().ToListAsync())
            .GroupBy(ub => ub.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(ub => ub.BranchId).Order().ToList());

        var summaries = users.Select(u => new UserSummary(
            u.Id,
            u.Email ?? string.Empty,
            u.IsActive,
            adminIds.Contains(u.Id),
            branchesByUser.GetValueOrDefault(u.Id, [])));

        return Ok(summaries);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserSummary>> Get(int id)
    {
        var user = await _tenantDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        return user is null ? NotFound() : Ok(await SummarizeAsync(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserSummary>> Create(CreateUserRequest request)
    {
        var branchError = await ValidateBranchIdsAsync(request.BranchIds);
        if (branchError is not null)
        {
            return branchError;
        }

        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "A user with this email already exists.");
        }

        await using var transaction = await _tenantDb.Database.BeginTransactionAsync();

        var user = new TenantUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            IsActive = true
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return IdentityFailure(created);
        }

        if (request.IsAdmin)
        {
            var roleResult = await _userManager.AddToRoleAsync(user, TenantRoles.AdminRoleName);
            if (!roleResult.Succeeded)
            {
                return IdentityFailure(roleResult);
            }
        }

        _tenantDb.UserBranches.AddRange(request.BranchIds.Distinct().Select(branchId =>
            new UserBranch { UserId = user.Id, BranchId = branchId }));
        await _tenantDb.SaveChangesAsync();

        await transaction.CommitAsync();

        return CreatedAtAction(nameof(Get), new { id = user.Id }, await SummarizeAsync(user));
    }

    // Replaces the user's whole branch set (not an append).
    [HttpPut("{id:int}/branches")]
    public async Task<ActionResult<UserSummary>> SetBranches(int id, SetUserBranchesRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var branchError = await ValidateBranchIdsAsync(request.BranchIds);
        if (branchError is not null)
        {
            return branchError;
        }

        var existing = await _tenantDb.UserBranches.Where(ub => ub.UserId == id).ToListAsync();
        _tenantDb.UserBranches.RemoveRange(existing);
        _tenantDb.UserBranches.AddRange(request.BranchIds.Distinct().Select(branchId =>
            new UserBranch { UserId = id, BranchId = branchId }));
        await _tenantDb.SaveChangesAsync();

        return Ok(await SummarizeAsync(user));
    }

    // Admins can't deactivate or demote themselves. Because only an active admin
    // can reach this controller, that also guarantees the tenant never loses its
    // last active admin through this API.
    [HttpPut("{id:int}/active")]
    public async Task<ActionResult<UserSummary>> SetActive(int id, SetUserActiveRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!request.IsActive && await IsSelfAsync(id))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot deactivate your own account.");
        }

        user.IsActive = request.IsActive;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        return Ok(await SummarizeAsync(user));
    }

    [HttpPut("{id:int}/admin")]
    public async Task<ActionResult<UserSummary>> SetAdmin(int id, SetUserAdminRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!request.IsAdmin && await IsSelfAsync(id))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot remove your own admin role.");
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, TenantRoles.AdminRoleName);
        if (request.IsAdmin && !isAdmin)
        {
            var result = await _userManager.AddToRoleAsync(user, TenantRoles.AdminRoleName);
            if (!result.Succeeded)
            {
                return IdentityFailure(result);
            }
        }
        else if (!request.IsAdmin && isAdmin)
        {
            var result = await _userManager.RemoveFromRoleAsync(user, TenantRoles.AdminRoleName);
            if (!result.Succeeded)
            {
                return IdentityFailure(result);
            }
        }

        return Ok(await SummarizeAsync(user));
    }

    [HttpPost("{id:int}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> ResetPassword(int id, ResetUserPasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        return NoContent();
    }

    private async Task<bool> IsSelfAsync(int id) => (await _userAccess.GetAsync()).UserId == id;

    private async Task<ActionResult?> ValidateBranchIdsAsync(IReadOnlyList<int> branchIds)
    {
        var distinct = branchIds.Distinct().ToList();
        var found = await _tenantDb.Branches.CountAsync(b => distinct.Contains(b.BranchId));

        return found == distinct.Count
            ? null
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: "One or more branches do not exist.");
    }

    private async Task<UserSummary> SummarizeAsync(TenantUser user)
    {
        var isAdmin = await _userManager.IsInRoleAsync(user, TenantRoles.AdminRoleName);
        var branchIds = await _tenantDb.UserBranches
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => ub.BranchId)
            .OrderBy(branchId => branchId)
            .ToListAsync();

        return new UserSummary(user.Id, user.Email ?? string.Empty, user.IsActive, isAdmin, branchIds);
    }

    private ActionResult IdentityFailure(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }

        return ValidationProblem(ModelState);
    }
}
