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
    ITenantContext tenantContext,
    CreatorFlowDbContext? db,
    IConfiguration configuration,
    IWebHostEnvironment environment) : IMiddleware
{
    public const string TenantHeader = "X-CreatorFlow-Tenant";
    private static readonly Guid DevelopmentTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
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
            if (bootstrapAllowed && tenantId == DevelopmentTenantId && db is not null)
            {
                var bootstrapTenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(
                    x => x.Id == tenantId && x.Status == TenantStatus.Active, context.RequestAborted);
                if (bootstrapTenant is null && !await db.Tenants.AnyAsync(x => x.Id == tenantId, context.RequestAborted))
                {
                    var created = Tenant.Create("Legacy workspace", "legacy-workspace", DateTimeOffset.UtcNow);
                    db.Entry(created).Property(x => x.Id).CurrentValue = tenantId;
                    db.Tenants.Add(created);
                    await db.SaveChangesAsync(context.RequestAborted);
                    bootstrapTenant = created;
                }
                if (bootstrapTenant is not null)
                {
                    var configuredRole = configuration["Tenant:BootstrapRole"];
                    var role = Enum.TryParse<TenantRole>(configuredRole, true, out var parsedRole) &&
                               Enum.IsDefined(parsedRole)
                        ? parsedRole
                        : TenantRole.Owner;
                    ((TenantContext)tenantContext).Set(tenantId, role.ToString());
                }
            }

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
