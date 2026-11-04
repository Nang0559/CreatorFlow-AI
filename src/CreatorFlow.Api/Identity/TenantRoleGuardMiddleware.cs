using CreatorFlow.Infrastructure.Persistence;

namespace CreatorFlow.Api.Identity;

/// <summary>
/// Applies a conservative tenant-role matrix to mutations. Endpoint handlers may
/// impose stricter checks; this middleware is the common baseline, not a substitute
/// for resource-level authorization.
/// </summary>
public sealed class TenantRoleGuardMiddleware(RequestDelegate next)
{
    private static readonly PathString[] EditorMutationPrefixes =
    [
        "/api/v1/content-projects",
        "/api/v1/topics",
        "/api/v1/douyin/channels",
        "/api/v1/douyin/videos",
        "/api/v1/share-inbox",
        "/api/v1/vietnam-trends",
        "/api/v1/style-experiments",
        "/api/v1/style-profiles",
        "/api/v1/ai/analyze",
        "/api/v1/ai/style-lab/generate-variants",
        "/api/v1/video",
        "/api/v1/discovery/search"
    ];

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var isMutation = HttpMethods.IsPost(context.Request.Method) ||
                         HttpMethods.IsPut(context.Request.Method) ||
                         HttpMethods.IsPatch(context.Request.Method) ||
                         HttpMethods.IsDelete(context.Request.Method);
        if (!tenantContext.IsResolved || !isMutation)
        {
            await next(context);
            return;
        }

        var role = tenantContext.Role;
        // Learning proposal approval/rejection changes the active creator profile.
        // Until a dedicated Reviewer membership role and audit workflow exist,
        // only workspace Owner/Admin may perform this sensitive review action.
        var isLearningProposalReview = context.Request.Path.StartsWithSegments(
            "/api/v1/style-profiles", out var remaining) &&
            (remaining.Value?.Contains("/learning-proposals/", StringComparison.OrdinalIgnoreCase) == true) &&
            (remaining.Value?.EndsWith("/approve", StringComparison.OrdinalIgnoreCase) == true ||
             remaining.Value?.EndsWith("/reject", StringComparison.OrdinalIgnoreCase) == true);

        var allowed = isLearningProposalReview
            ? role is "Owner" or "Admin"
            : role switch
        {
            "Owner" or "Admin" => true,
            "Editor" => EditorMutationPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)),
            "Publisher" => context.Request.Path.StartsWithSegments("/api/v1/publishing"),
            "Analyst" => false,
            _ => false
        };

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "This workspace role does not permit this operation.",
                role
            });
            return;
        }

        await next(context);
    }
}
