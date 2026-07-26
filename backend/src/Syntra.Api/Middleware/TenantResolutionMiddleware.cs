using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Platform;
using Syntra.Infrastructure.Platform;

namespace Syntra.Api.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, PlatformDbContext platformDb, CurrentTenant currentTenant)
    {
        var slug = context.Request.Host.Host.Split('.')[0].ToLowerInvariant();

        var tenant = await platformDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Tenant not found.");
            return;
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("This account is not currently active.");
            return;
        }

        currentTenant.Set(tenant);

        await _next(context);
    }
}
