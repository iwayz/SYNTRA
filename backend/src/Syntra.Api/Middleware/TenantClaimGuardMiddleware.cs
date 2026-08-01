using Syntra.Domain.Platform;

namespace Syntra.Api.Middleware;

// Runs after UseAuthentication(). If the request carries a valid JWT, its
// tenant_id claim must match the tenant resolved from the subdomain - stops a
// token issued for one tenant from being replayed against another tenant's
// subdomain. Requests with no token at all just pass through; UseAuthorization()
// decides separately whether the endpoint requires one.
public class TenantClaimGuardMiddleware
{
    private readonly RequestDelegate _next;

    public TenantClaimGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claimTenantId = context.User.FindFirst("tenant_id")?.Value;

            if (claimTenantId is null || !int.TryParse(claimTenantId, out var tenantId) || tenantId != currentTenant.TenantId)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Token is not valid for this tenant.");
                return;
            }
        }

        await _next(context);
    }
}
