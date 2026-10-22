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
        // The API's own self-description isn't tenant business data, so it
        // shouldn't require a valid tenant subdomain to view - exempt it
        // rather than gate every "generic" endpoint behind tenant lookup.
        if (context.Request.Path.StartsWithSegments("/openapi"))
        {
            await _next(context);
            return;
        }

        var slug = context.Request.Host.Host.Split('.')[0].ToLowerInvariant();

        var tenant = await platformDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Tenant not found.");
            return;
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled or TenantStatus.Provisioning)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("This account is not currently active.");
            return;
        }

        currentTenant.Set(tenant);

        await _next(context);
    }
}
