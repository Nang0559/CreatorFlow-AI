using System.Threading.RateLimiting;

namespace CreatorFlow.Api.Identity;

/// <summary>
/// Applies the authenticated subject/workspace quota after the JWT identity and
/// active tenant membership have been resolved. The earlier ASP.NET rate limiter
/// rejects abusive source IPs before authentication and database access.
/// </summary>
public sealed class TenantRateLimitMiddleware(
    RequestDelegate next,
    PartitionedRateLimiter<HttpContext> limiter,
    IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!(configuration.GetValue<bool?>("RateLimiting:Enabled") ?? true) ||
            context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        using var lease = await limiter.AcquireAsync(context, 1, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Workspace request rate limit exceeded. Retry shortly."
            }, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
