using Syntra.Api.Auth;

namespace Syntra.Api.Middleware;

// Runs after TenantClaimGuard. A valid, unexpired JWT is not enough: if the
// user has since been deactivated (or deleted) the token is rejected right
// away, instead of staying usable until it expires.
public class ActiveUserMiddleware
{
    private readonly RequestDelegate _next;

    public ActiveUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUserAccess userAccess)
    {
        if (context.User.Identity?.IsAuthenticated == true && !(await userAccess.GetAsync()).IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("User account is not active.");
            return;
        }

        await _next(context);
    }
}
