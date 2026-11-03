using System.Data;
using System.Security.Cryptography;
using System.Text;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Api.Identity;

public static class TenantAccountEndpoints
{
    public static void MapTenantAccountEndpoints(this WebApplication app)
    {
        var oidcOnly = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build();
        var group = app.MapGroup("/api/v1/identity");

        group.MapGet("/workspaces", async (HttpContext http, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var items = await (from m in db.TenantMemberships.AsNoTracking()
                               join t in db.Tenants.AsNoTracking() on m.TenantId equals t.Id
                               where m.IdentityIssuer == issuer && m.SubjectId == subject &&
                                     m.Status == TenantMembershipStatus.Active && t.Status == TenantStatus.Active
                               orderby t.Name
                               select new WorkspaceItem(t.Id, t.Name, t.Slug, m.Role.ToString())).ToArrayAsync(ct);
            return Results.Ok(items);
        }).RequireAuthorization(oidcOnly);

        group.MapPost("/owner/bootstrap", async (BootstrapOwnerRequest request, HttpContext http,
            IConfiguration config, IHostEnvironment env, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            if (!http.Request.IsHttps && !env.IsDevelopment()) return Results.Forbid();
            var expected = config["Authentication:OwnerBootstrapToken"];
            var supplied = http.Request.Headers["X-CreatorFlow-Owner-Bootstrap"].ToString();
            if (string.IsNullOrWhiteSpace(expected) || !FixedTimeEquals(expected, supplied)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["workspace"] = ["Workspace name and slug are required."] });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (await db.TenantMemberships.AnyAsync(x => x.Role == TenantRole.Owner && x.Status == TenantMembershipStatus.Active, ct))
                return Results.Conflict(new { error = "An active Owner already exists. Initial Owner bootstrap is globally one-time." });
            var slug = request.Slug.Trim().ToLowerInvariant();
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == slug, ct);
            if (tenant is null)
            {
                try { tenant = Tenant.Create(request.Name, slug, DateTimeOffset.UtcNow); }
                catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["workspace"] = [ex.Message] }); }
                db.Tenants.Add(tenant);
            }
            if (await db.TenantMemberships.AnyAsync(x => x.TenantId == tenant.Id &&
                x.IdentityIssuer == issuer && x.SubjectId == subject, ct))
                return Results.Conflict(new { error = "This identity already has a membership in this workspace." });
            var verifiedOwnerEmail = string.Equals(http.User.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase) ? http.User.FindFirst("email")?.Value : null;
            db.TenantMemberships.Add(TenantMembership.Create(tenant.Id, issuer, subject, TenantRole.Owner, DateTimeOffset.UtcNow, verifiedOwnerEmail));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created("/api/v1/identity/workspaces", new WorkspaceItem(tenant.Id, tenant.Name, tenant.Slug, "Owner"));
        }).RequireAuthorization(oidcOnly);

        group.MapPost("/workspaces/{tenantId:guid}/invitations", async (Guid tenantId, CreateTenantInvitationRequest request,
            HttpContext http, TenantContext tenantContext, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var actor = await db.TenantMemberships.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject && x.Status == TenantMembershipStatus.Active, ct);
            if (actor is null || (actor.Role != TenantRole.Owner && actor.Role != TenantRole.Admin)) return Results.Forbid();
            if (!Enum.TryParse<TenantRole>(request.Role, true, out var role) || role == TenantRole.Owner || !Enum.IsDefined(role))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Allowed invite roles: Admin, Editor, Analyst, Publisher. Owner cannot be invited."] });
            if (actor.Role == TenantRole.Admin && role == TenantRole.Admin) return Results.Forbid();
            if (!await db.Tenants.AnyAsync(x => x.Id == tenantId && x.Status == TenantStatus.Active, ct)) return Results.NotFound();
            tenantContext.Set(tenantId, actor.Role.ToString());
            var raw = CreateToken();
            var now = DateTimeOffset.UtcNow;
            TenantInvitation invitation;
            try { invitation = TenantInvitation.Create(tenantId, request.Email, role, HashToken(raw), now.AddHours(72), issuer, subject, now); }
            catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["invitation"] = [ex.Message] }); }
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.TenantInvitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            await Audit(db, tenantId, issuer, subject, "InvitationCreated", null, null,
                new { invitation.Id, invitation.Email, role = role.ToString(), invitation.ExpiresAt }, now, ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/identity/workspaces/{tenantId}/invitations/{invitation.Id}",
                new { invitation.Id, invitation.Email, role = role.ToString(), invitation.ExpiresAt, token = raw });
        }).RequireAuthorization(oidcOnly);

        group.MapGet("/workspaces/{tenantId:guid}/members", async (Guid tenantId, HttpContext http,
            TenantContext tenantContext, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var actor = await db.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject && x.Status == TenantMembershipStatus.Active, ct);
            if (actor is null || (actor.Role != TenantRole.Owner && actor.Role != TenantRole.Admin)) return Results.Forbid();
            tenantContext.Set(tenantId, actor.Role.ToString());
            var rows = await db.TenantMemberships.AsNoTracking().Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.CreatedAt).Select(x => new MemberItem(x.Id, x.Role.ToString(), x.Status.ToString(), x.CreatedAt)).ToArrayAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization(oidcOnly);

        group.MapGet("/workspaces/{tenantId:guid}/invitations", async (Guid tenantId, HttpContext http,
            TenantContext tenantContext, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var actor = await db.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject && x.Status == TenantMembershipStatus.Active, ct);
            if (actor is null || (actor.Role != TenantRole.Owner && actor.Role != TenantRole.Admin)) return Results.Forbid();
            tenantContext.Set(tenantId, actor.Role.ToString());
            var rows = await db.TenantInvitations.AsNoTracking().Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new InvitationItem(x.Id, x.Email, x.Role.ToString(), x.Status.ToString(), x.CreatedAt, x.ExpiresAt)).ToArrayAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization(oidcOnly);

        group.MapPost("/invitations/accept", async (AcceptTenantInvitationRequest request, HttpContext http,
            TenantContext tenantContext, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var verified = string.Equals(http.User.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var email = http.User.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
            if (!verified || string.IsNullOrWhiteSpace(email)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 256) return Results.BadRequest();

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = HashToken(request.Token);
            var invitation = await db.TenantInvitations.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.TokenHash == hash && x.Status == TenantInvitationStatus.Pending &&
                x.ExpiresAt > DateTimeOffset.UtcNow && x.RevokedAt == null, ct);
            if (invitation is null) return Results.NotFound();
            if (!string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (await db.TenantMemberships.AnyAsync(x => x.TenantId == invitation.TenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject, ct))
                return Results.Conflict(new { error = "This identity already has a membership in this workspace." });
            tenantContext.Set(invitation.TenantId, invitation.Role.ToString());
            var now = DateTimeOffset.UtcNow;
            invitation.Accept(issuer, subject, now);
            db.TenantMemberships.Add(TenantMembership.Create(invitation.TenantId, issuer, subject, invitation.Role, now, email));
            await db.SaveChangesAsync(ct);
            await Audit(db, invitation.TenantId, issuer, subject, "InvitationAccepted", issuer, subject,
                new { invitation.Id, invitation.Role }, now, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { tenantId = invitation.TenantId, role = invitation.Role.ToString() });
        }).RequireAuthorization(oidcOnly);

        group.MapDelete("/workspaces/{tenantId:guid}/invitations/{invitationId:guid}", async (Guid tenantId,
            Guid invitationId, HttpContext http, TenantContext tenantContext, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var actor = await db.TenantMemberships.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject && x.Status == TenantMembershipStatus.Active, ct);
            if (actor is null || (actor.Role != TenantRole.Owner && actor.Role != TenantRole.Admin)) return Results.Forbid();
            tenantContext.Set(tenantId, actor.Role.ToString());
            var invitation = await db.TenantInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.TenantId == tenantId, ct);
            if (invitation is null) return Results.NotFound();
            invitation.Revoke(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            await Audit(db, tenantId, issuer, subject, "InvitationRevoked", null, null, new { invitation.Id }, DateTimeOffset.UtcNow, ct);
            return Results.NoContent();
        }).RequireAuthorization(oidcOnly);

        group.MapPut("/workspaces/{tenantId:guid}/members/{membershipId:guid}/role", async (Guid tenantId,
            Guid membershipId, ChangeTenantRoleRequest request, HttpContext http, TenantContext tenantContext,
            CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var actor = await db.TenantMemberships.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject && x.Status == TenantMembershipStatus.Active, ct);
            if (actor is null || actor.Role != TenantRole.Owner) return Results.Forbid();
            if (!Enum.TryParse<TenantRole>(request.Role, true, out var role) || !Enum.IsDefined(role))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Unknown workspace role."] });
            var target = await db.TenantMemberships.SingleOrDefaultAsync(x => x.Id == membershipId && x.TenantId == tenantId, ct);
            if (target is null) return Results.NotFound();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (target.Role == TenantRole.Owner && role != TenantRole.Owner &&
                await db.TenantMemberships.CountAsync(x => x.TenantId == tenantId && x.Role == TenantRole.Owner &&
                    x.Status == TenantMembershipStatus.Active, ct) <= 1)
                return Results.Conflict(new { error = "A workspace must retain at least one active Owner." });
            tenantContext.Set(tenantId, actor.Role.ToString());
            var oldRole = target.Role.ToString();
            target.ChangeRole(role, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            await Audit(db, tenantId, issuer, subject, "MembershipRoleChanged", target.IdentityIssuer, target.SubjectId,
                new { oldRole, newRole = role.ToString() }, DateTimeOffset.UtcNow, ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(oidcOnly);

        group.MapDelete("/workspaces/{tenantId:guid}/members/{membershipId:guid}", async (Guid tenantId,
            Guid membershipId, HttpContext http, TenantContext tenantContext, CreatorFlowDbContext? db, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (!TryIdentity(http, out var issuer, out var subject)) return Results.Forbid();
            var actor = await db.TenantMemberships.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.IdentityIssuer == issuer && x.SubjectId == subject && x.Status == TenantMembershipStatus.Active, ct);
            if (actor is null || actor.Role != TenantRole.Owner) return Results.Forbid();
            var target = await db.TenantMemberships.SingleOrDefaultAsync(x => x.Id == membershipId && x.TenantId == tenantId, ct);
            if (target is null) return Results.NotFound();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (target.Role == TenantRole.Owner && target.Status == TenantMembershipStatus.Active &&
                await db.TenantMemberships.CountAsync(x => x.TenantId == tenantId && x.Role == TenantRole.Owner &&
                    x.Status == TenantMembershipStatus.Active, ct) <= 1)
                return Results.Conflict(new { error = "The last active Owner cannot be suspended." });
            tenantContext.Set(tenantId, actor.Role.ToString());
            target.Suspend(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            await Audit(db, tenantId, issuer, subject, "MembershipSuspended", target.IdentityIssuer, target.SubjectId,
                new { target.Id }, DateTimeOffset.UtcNow, ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(oidcOnly);
    }

    private static bool TryIdentity(HttpContext c, out string issuer, out string subject)
    {
        issuer = c.User.FindFirst("iss")?.Value ?? "";
        subject = c.User.FindFirst("sub")?.Value ?? "";
        return c.User.Identity?.IsAuthenticated == true && issuer.Length is > 0 and <= 500 &&
               subject.Length is > 0 and <= 200;
    }
    private static bool FixedTimeEquals(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }
    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static Task<int> Audit(CreatorFlowDbContext db, Guid tenantId, string actorIssuer, string actorSubject,
        string type, string? targetIssuer, string? targetSubject, object details, DateTimeOffset at, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(details);
        return db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT dbo.TenantSecurityAudit
(TenantId, ActorIssuer, ActorSubject, EventType, TargetIssuer, TargetSubject, DetailsJson, OccurredAt)
VALUES ({tenantId}, {actorIssuer}, {actorSubject}, {type}, {targetIssuer}, {targetSubject}, {json}, {at});", ct);
    }
}
public sealed record BootstrapOwnerRequest(string Name, string Slug);
public sealed record CreateTenantInvitationRequest(string Email, string Role);
public sealed record AcceptTenantInvitationRequest(string Token);
public sealed record ChangeTenantRoleRequest(string Role);
public sealed record WorkspaceItem(Guid Id, string Name, string Slug, string Role);
public sealed record MemberItem(Guid Id, string? VerifiedEmail, string Role, string Status, DateTimeOffset CreatedAt);
public sealed record InvitationItem(Guid Id, string Email, string Role, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
