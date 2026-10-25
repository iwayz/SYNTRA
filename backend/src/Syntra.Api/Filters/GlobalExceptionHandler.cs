using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Syntra.Infrastructure.Inventory;

namespace Syntra.Api.Filters;

// Ensures no unhandled exception ever reaches the client as a raw stack
// trace. Maps known cases (unique constraint violations) to a friendly
// status; everything else becomes a generic, non-leaking 500.
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception");

        var (statusCode, title) = exception switch
        {
            StockLockTimeoutException
                => (StatusCodes.Status409Conflict, "Stock for this item is being updated by another request. Please retry."),
            DbUpdateConcurrencyException
                => (StatusCodes.Status409Conflict, "This record was changed by someone else while you were working. Reload and try again."),
            DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx)
                => (StatusCodes.Status409Conflict, "A record with the same value already exists."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = statusCode, Title = title },
            cancellationToken);

        return true;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
