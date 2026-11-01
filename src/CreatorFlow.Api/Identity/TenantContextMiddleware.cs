using System.Security.Claims;
using CreatorFlow.Api.Security;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Api.Identity;

/// <summary>
/// Resolves the selected workspace from a signed OIDC identity plus an active database membership.
/// X-CreatorFlow-Tenant is only a selector; it never grants access by itself.
/// </summary>
public sealed class TenantContextMiddleware(
    RequestDelegate next,
    ITenantContext tenantContext,
    CreatorFlowDbContext? db,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    public const string TenantHeader = "X-CreatorFlow-Tenant";
    private static readonly Guid DevelopmentTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");

    public async Task InvokeAsync(HttpContext context)
    {
        var identity = context.User.Identity;
        if (identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // The shared API key is a local bootstrap mechanism only. It cannot select arbitrary tenants
        // and is never accepted as a SaaS user identity.
        if (identity.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName)
        {
            var bootstrapAllowed = environment.IsDevelopment() &&
                (configuration.GetValue<bool?>("Tenant:AllowApiKeyBootstrap") ?? true);
            var configuredId = configuration["Tenant:BootstrapTenantId"];
            var tenantId = Guid.TryParse(configuredId, out var parsed) ? parsed : DevelopmentTenantId;
            if (bootstrapAllowed && tenantId == DevelopmentTenantId)
                ((TenantContext)tenantContext).Set(tenantId, TenantRole.Owner.ToString());

            await next(context);
            return;
        }

        if (db is null)
        {
            await next(context);
            return;
        }

        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        var selectedTenant = context.Request.Headers[TenantHeader].ToString();

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
            !Guid.TryParse(selectedTenant, out var tenantIdFromHeader) || tenantIdFromHeader == Guid.Empty)
        {
            await next(context);
            return;
        }

        var membership = await db.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenantIdFromHeader &&
            x.IdentityIssuer == issuer &&
            x.SubjectId == subject &&
            x.Status == TenantMembershipStatus.Active, context.RequestAborted);

        if (membership is null)
        {
            await next(context);
            return;
        }

        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == membership.TenantId && x.Status == TenantStatus.Active, context.RequestAborted);

        if (tenant is not null)
            ((TenantContext)tenantContext).Set(tenant.Id, membership.Role.ToString());

        await next(context);
    }
}
