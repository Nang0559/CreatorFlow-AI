using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Api.Discovery;

public static class DiscoveryConnectorSettingsEndpoints
{
    private const string BravePurpose = "CreatorFlow.Discovery.BraveApiKey.v1";
    private const string TikTokKeyPurpose = "CreatorFlow.Discovery.TikTokResearchClientKey.v1";
    private const string TikTokSecretPurpose = "CreatorFlow.Discovery.TikTokResearchClientSecret.v1";

    public static IEndpointRouteBuilder MapDiscoveryConnectorSettings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/discovery/connectors").RequireAuthorization();
        group.MapGet("/settings", async (CreatorFlowDbContext? db, ITenantContext tenant, IConfiguration config, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var row = await db.DiscoveryConnectorConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct);
            return Results.Ok(new DiscoveryConnectorSettingsResponse(
                row?.EncryptedBraveApiKey is not null || !string.IsNullOrWhiteSpace(config["BraveSearch:ApiKey"]),
                row?.EncryptedTikTokResearchClientKey is not null || !string.IsNullOrWhiteSpace(config["TikTokResearch:ClientKey"]),
                row?.EncryptedTikTokResearchClientSecret is not null || !string.IsNullOrWhiteSpace(config["TikTokResearch:ClientSecret"]),
                row is not null));
        });
        group.MapPut("/settings", async (DiscoveryConnectorSettingsWriteRequest request, CreatorFlowDbContext? db,
            ITenantContext tenant, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (request.BraveApiKey?.Length > 500 || request.TikTokResearchClientKey?.Length > 500 ||
                request.TikTokResearchClientSecret?.Length > 1000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Khóa vượt quá độ dài cho phép."] });

            var row = await db.DiscoveryConnectorConfigurations
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct);
            if (row is null)
            {
                row = new DiscoveryConnectorConfiguration();
                db.DiscoveryConnectorConfigurations.Add(row);
            }

            var brave = protection.CreateProtector(BravePurpose);
            var tiktokKey = protection.CreateProtector(TikTokKeyPurpose);
            var tiktokSecret = protection.CreateProtector(TikTokSecretPurpose);
            if (request.ClearBraveApiKey) row.EncryptedBraveApiKey = null;
            else if (!string.IsNullOrWhiteSpace(request.BraveApiKey)) row.EncryptedBraveApiKey = brave.Protect(request.BraveApiKey.Trim());
            if (request.ClearTikTokResearchCredentials)
            {
                row.EncryptedTikTokResearchClientKey = null;
                row.EncryptedTikTokResearchClientSecret = null;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(request.TikTokResearchClientKey))
                    row.EncryptedTikTokResearchClientKey = tiktokKey.Protect(request.TikTokResearchClientKey.Trim());
                if (!string.IsNullOrWhiteSpace(request.TikTokResearchClientSecret))
                    row.EncryptedTikTokResearchClientSecret = tiktokSecret.Protect(request.TikTokResearchClientSecret.Trim());
            }
            row.UpdatedAt = DateTimeOffset.UtcNow;
            if ((row.EncryptedTikTokResearchClientKey is null) != (row.EncryptedTikTokResearchClientSecret is null))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["tiktok"] = ["Client Key và Client Secret phải được cấu hình cùng nhau."] });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new DiscoveryConnectorSettingsResponse(
                row.EncryptedBraveApiKey is not null, row.EncryptedTikTokResearchClientKey is not null,
                row.EncryptedTikTokResearchClientSecret is not null, true));
        });
        return app;
    }

    public static async Task<(string? BraveApiKey, string? TikTokClientKey, string? TikTokClientSecret)> ResolveAsync(
        CreatorFlowDbContext? db, ITenantContext tenant, IDataProtectionProvider protection, IConfiguration config,
        CancellationToken ct)
    {
        var row = db is null ? null : await db.DiscoveryConnectorConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct);
        string? Unprotect(string? value, string purpose) =>
            string.IsNullOrWhiteSpace(value) ? null : protection.CreateProtector(purpose).Unprotect(value);
        return (
            Unprotect(row?.EncryptedBraveApiKey, BravePurpose) ?? config["BraveSearch:ApiKey"],
            Unprotect(row?.EncryptedTikTokResearchClientKey, TikTokKeyPurpose) ?? config["TikTokResearch:ClientKey"],
            Unprotect(row?.EncryptedTikTokResearchClientSecret, TikTokSecretPurpose) ?? config["TikTokResearch:ClientSecret"]);
    }

    private static bool CanManage(ITenantContext tenant) => tenant.IsResolved &&
        (string.Equals(tenant.Role, "Owner", StringComparison.Ordinal) || string.Equals(tenant.Role, "Admin", StringComparison.Ordinal));
}

public sealed record DiscoveryConnectorSettingsWriteRequest(string? BraveApiKey, string? TikTokResearchClientKey,
    string? TikTokResearchClientSecret, bool ClearBraveApiKey = false, bool ClearTikTokResearchCredentials = false);
public sealed record DiscoveryConnectorSettingsResponse(bool HasBraveApiKey, bool HasTikTokResearchClientKey,
    bool HasTikTokResearchClientSecret, bool IsStoredInWorkspace);
