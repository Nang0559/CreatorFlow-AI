using Microsoft.AspNetCore.Authorization;
using CreatorFlow.Infrastructure.Persistence;

namespace CreatorFlow.Api.Identity;

public sealed class TenantMembershipRequirement : IAuthorizationRequirement { }

public sealed class TenantMembershipAuthorizationHandler(ITenantContext tenantContext)
    : AuthorizationHandler<TenantMembershipRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        TenantMembershipRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && tenantContext.IsResolved)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
