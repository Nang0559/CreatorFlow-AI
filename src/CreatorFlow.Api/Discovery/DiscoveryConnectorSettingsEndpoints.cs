using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace CreatorFlow.Api.Discovery;

public static class DiscoveryConnectorSettingsEndpoints
{
    private const string BravePurpose = "CreatorFlow.Discovery.BraveApiKey.v1";
    private const string TikTokKeyPurpose = "CreatorFlow.Discovery.TikTokResearchClientKey.v1";
    private const string TikTokSecretPurpose = "CreatorFlow.Discovery.TikTokResearchClientSecret.v1";

    public static IEndpointRouteBuilder MapDiscoveryConnectorSettings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/discovery/connectors").RequireAuthorization("TenantOwnerOrAdmin");
        group.MapGet("/settings", async (CreatorFlowDbContext? db, ITenantContext tenant, IConfiguration config, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var row = await db.DiscoveryConnectorConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct);
            return Results.Ok(new DiscoveryConnectorSettingsResponse(
                row?.EncryptedBraveApiKey is not null || !string.IsNullOrWhiteSpace(config["BraveSearch:ApiKey"]),
                row?.EncryptedTikTokResearchClientKey is not null || !string.IsNullOrWhiteSpace(config["TikTokResearch:ClientKey"]),
                row?.EncryptedTikTokResearchClientSecret is not null || !string.IsNullOrWhiteSpace(config["TikTokResearch:ClientSecret"]),
                row is not null));
        });
        group.MapGet("/custom-providers", async (CreatorFlowDbContext? db, ITenantContext tenant,
            IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var row = await db.DiscoveryConnectorConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct);
            var providers = ReadCustomProviders(row, protection);
            return Results.Ok(providers.Select(x => new CustomProviderResponse(
                x.Id, x.Name, x.Enabled, x.Endpoint, x.QueryParameter, x.QueryTemplate,
                x.ResultsPath, x.UrlPath, x.TitlePath, x.DescriptionPath,
                x.ApiKey is not null, x.ApiKeyHeader, x.ApiKeyPrefix, x.Accept,
                x.TimeoutSeconds, x.MaxQueries)).ToArray());
        });
        group.MapPut("/custom-providers", async (CustomProviderWriteRequest[] request, CreatorFlowDbContext? db,
            ITenantContext tenant, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            if (request.Length > 20 || request.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 80 ||
                string.IsNullOrWhiteSpace(x.Endpoint) || x.Endpoint.Length > 1000 ||
                x.ApiKey?.Length > 2000 || x.QueryParameter?.Length > 80 ||
                x.ResultsPath?.Length > 200 || x.UrlPath?.Length > 200))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["providers"] = ["Tối đa 20 provider; tên, endpoint và các trường cấu hình phải hợp lệ."] });
            if (request.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Length)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["providers"] = ["Tên provider không được trùng nhau."] });
            foreach (var item in request)
                if (!Uri.TryCreate(item.Endpoint, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["endpoint"] = ["Endpoint phải là URL HTTP(S) hợp lệ."] });

            var row = await db.DiscoveryConnectorConfigurations
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct);
            if (row is null)
            {
                row = new DiscoveryConnectorConfiguration();
                db.DiscoveryConnectorConfigurations.Add(row);
            }
            var existing = ReadCustomProviders(row, protection).ToDictionary(x => x.Id);
            var saved = request.Select(x =>
            {
                var id = x.Id ?? Guid.NewGuid();
                existing.TryGetValue(id, out var prior);
                return new ConfiguredSearchProvider
                {
                    Id = id, Name = x.Name.Trim(), Enabled = x.Enabled, Endpoint = x.Endpoint.Trim(),
                    QueryParameter = x.QueryParameter ?? "q", QueryTemplate = x.QueryTemplate ?? "{topic} {query}",
                    ResultsPath = x.ResultsPath ?? "results", UrlPath = x.UrlPath ?? "url",
                    TitlePath = x.TitlePath ?? "title", DescriptionPath = x.DescriptionPath ?? "content",
                    ApiKey = string.IsNullOrWhiteSpace(x.ApiKey) ? prior?.ApiKey : x.ApiKey.Trim(),
                    ApiKeyHeader = x.ApiKeyHeader, ApiKeyPrefix = x.ApiKeyPrefix, Accept = x.Accept ?? "application/json",
                    TimeoutSeconds = Math.Clamp(x.TimeoutSeconds, 3, 45), MaxQueries = Math.Clamp(x.MaxQueries, 1, 6)
                };
            }).ToArray();
            var protector = protection.CreateProtector("CreatorFlow.Discovery.CustomProviders.v1");
            row.EncryptedCustomProvidersJson = protector.Protect(JsonSerializer.Serialize(saved));
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(saved.Select(x => new CustomProviderResponse(
                x.Id, x.Name, x.Enabled, x.Endpoint, x.QueryParameter, x.QueryTemplate,
                x.ResultsPath, x.UrlPath, x.TitlePath, x.DescriptionPath,
                x.ApiKey is not null, x.ApiKeyHeader, x.ApiKeyPrefix, x.Accept,
                x.TimeoutSeconds, x.MaxQueries)).ToArray());
        });

        group.MapPut("/settings", async (DiscoveryConnectorSettingsWriteRequest request, CreatorFlowDbContext? db,
            ITenantContext tenant, IDataProtectionProvider protection, CancellationToken ct) =>
        {
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

    public static ConfiguredSearchProvider[] ReadCustomProviders(DiscoveryConnectorConfiguration? row, IDataProtectionProvider protection)
    {
        if (string.IsNullOrWhiteSpace(row?.EncryptedCustomProvidersJson)) return [];
        try
        {
            var json = protection.CreateProtector("CreatorFlow.Discovery.CustomProviders.v1")
                .Unprotect(row.EncryptedCustomProvidersJson);
            return JsonSerializer.Deserialize<ConfiguredSearchProvider[]>(json) ?? [];
        }
        catch (CryptographicException) { return []; }
        catch (JsonException) { return []; }
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

}

public sealed record DiscoveryConnectorSettingsWriteRequest(string? BraveApiKey, string? TikTokResearchClientKey,
    string? TikTokResearchClientSecret, bool ClearBraveApiKey = false, bool ClearTikTokResearchCredentials = false);
public sealed record DiscoveryConnectorSettingsResponse(bool HasBraveApiKey, bool HasTikTokResearchClientKey,
    bool HasTikTokResearchClientSecret, bool IsStoredInWorkspace);

public sealed record CustomProviderWriteRequest(Guid? Id, string Name, bool Enabled, string Endpoint,
    string? QueryParameter, string? QueryTemplate, string? ResultsPath, string? UrlPath,
    string? TitlePath, string? DescriptionPath, string? ApiKey, string? ApiKeyHeader,
    string? ApiKeyPrefix, string? Accept, int TimeoutSeconds = 15, int MaxQueries = 4);
public sealed record CustomProviderResponse(Guid Id, string Name, bool Enabled, string Endpoint,
    string? QueryParameter, string? QueryTemplate, string? ResultsPath, string? UrlPath,
    string? TitlePath, string? DescriptionPath, bool HasApiKey, string? ApiKeyHeader,
    string? ApiKeyPrefix, string? Accept, int TimeoutSeconds, int MaxQueries);
