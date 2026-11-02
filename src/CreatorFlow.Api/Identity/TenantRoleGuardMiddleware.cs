using CreatorFlow.Infrastructure.Persistence;

namespace CreatorFlow.Api.Identity;

/// <summary>Enforces baseline role boundaries for all tenant-scoped API mutations.</summary>
public sealed class TenantRoleGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var isMutation = HttpMethods.IsPost(context.Request.Method) ||
                         HttpMethods.IsPut(context.Request.Method) ||
                         HttpMethods.IsPatch(context.Request.Method) ||
                         HttpMethods.IsDelete(context.Request.Method);
        if (tenantContext.IsResolved && isMutation &&
            (string.Equals(tenantContext.Role, "Analyst", StringComparison.Ordinal) ||
             (string.Equals(tenantContext.Role, "Publisher", StringComparison.Ordinal) &&
              !context.Request.Path.StartsWithSegments("/api/v1/publishing"))))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "This workspace role does not permit this operation." });
            return;
        }
        await next(context);
    }
}
