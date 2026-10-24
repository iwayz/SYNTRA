using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.TenantData;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Auth;

// What the signed-in user may do right now, read from the tenant database
// rather than from the JWT. Tokens last 60 minutes and there are no refresh
// tokens, so anything in the token (role, branches, even "still active") can be
// stale; revoking access has to take effect on the next request.
public record UserAccess(int UserId, bool IsActive, bool IsAdmin, IReadOnlyList<int> BranchIds);

public interface ICurrentUserAccess
{
    Task<UserAccess> GetAsync();
}

// Scoped: loads at most once per request and shares the result between the
// active-user middleware, the TenantAdmin policy and controllers.
public class CurrentUserAccess : ICurrentUserAccess
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantDbContext _tenantDb;
    private Task<UserAccess>? _loading;

    public CurrentUserAccess(IHttpContextAccessor httpContextAccessor, TenantDbContext tenantDb)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantDb = tenantDb;
    }

    public Task<UserAccess> GetAsync() => _loading ??= LoadAsync();

    private async Task<UserAccess> LoadAsync()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
        {
            return new UserAccess(0, false, false, []);
        }

        var isActive = await _tenantDb.Users
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsActive)
            .FirstOrDefaultAsync();

        // A user deleted since the token was issued counts as inactive.
        if (isActive != true)
        {
            return new UserAccess(userId, false, false, []);
        }

        var isAdmin = await _tenantDb.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == TenantRoles.AdminRoleId);

        var branchIds = await _tenantDb.UserBranches
            .Where(ub => ub.UserId == userId)
            .Select(ub => ub.BranchId)
            .ToListAsync();

        return new UserAccess(userId, true, isAdmin, branchIds);
    }
}

public static class AuthPolicies
{
    public const string TenantAdmin = "TenantAdmin";
}

public class TenantAdminRequirement : IAuthorizationRequirement
{
}

public class TenantAdminHandler : AuthorizationHandler<TenantAdminRequirement>
{
    private readonly ICurrentUserAccess _access;

    public TenantAdminHandler(ICurrentUserAccess access)
    {
        _access = access;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantAdminRequirement requirement)
    {
        var access = await _access.GetAsync();

        if (access.IsActive && access.IsAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
