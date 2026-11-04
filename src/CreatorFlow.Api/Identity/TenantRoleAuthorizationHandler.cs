using Microsoft.AspNetCore.Authorization;
using CreatorFlow.Infrastructure.Persistence;

namespace CreatorFlow.Api.Identity;

public sealed class TenantRoleRequirement(params string[] allowedRoles) : IAuthorizationRequirement
{
    public IReadOnlySet<string> AllowedRoles { get; } =
        new HashSet<string>(allowedRoles, StringComparer.Ordinal);
}

public sealed class TenantRoleAuthorizationHandler(ITenantContext tenantContext)
    : AuthorizationHandler<TenantRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            tenantContext.IsResolved &&
            requirement.AllowedRoles.Contains(tenantContext.Role))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
