using System.Net.Http.Headers;
using System.Text.Json;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

public static class AIProviderRegistryEndpoints
{
    public static IServiceCollection AddAIProviderRegistry(this IServiceCollection services, IConfiguration configuration)
    {
        var keyPath = configuration["CREATORFLOW_DATA_PROTECTION_KEYS_PATH"];
        if (string.IsNullOrWhiteSpace(keyPath))
            keyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorFlow", "DataProtectionKeys");
        Directory.CreateDirectory(keyPath);
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("CreatorFlow.Api")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }
        return services;
    }

    public static IEndpointRouteBuilder MapAIProviderRegistry(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/providers").RequireAuthorization();
        group.MapGet("/runtime", (IConfiguration config, ITenantContext tenant) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            var names = new[] { "gemini", "openai", "ollama" };
            var rows = names.Select(name =>
            {
                var prefix = $"CREATORFLOW_LLM_{name.ToUpperInvariant()}_";
                var legacyGemini = name == "gemini";
                var key = config[prefix + "API_KEY"] ?? (legacyGemini ? config["CREATORFLOW_LLM_API_KEY"] : null);
                var model = config[prefix + "MODEL"] ?? (legacyGemini ? config["CREATORFLOW_LLM_MODEL"] : null);
                var url = config[prefix + "BASE_URL"] ?? (legacyGemini ? config["CREATORFLOW_LLM_BASE_URL"] : null);
                return new RuntimeAIProvider(name, model, url, !string.IsNullOrWhiteSpace(key),
                    string.Equals(config["CREATORFLOW_LLM_PRIMARY_PROVIDER"] ?? "gemini", name, StringComparison.OrdinalIgnoreCase),
                    (config["CREATORFLOW_LLM_FALLBACK_PROVIDERS"] ?? "openai").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(name, StringComparer.OrdinalIgnoreCase));
            }).Where(x => x.HasApiKey || !string.IsNullOrWhiteSpace(x.Model) || !string.IsNullOrWhiteSpace(x.BaseUrl)).ToArray();
            return Results.Ok(rows);
        });
        group.MapGet("/", async (CreatorFlowDbContext? db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var rows = await db.AIProviderConfigurations.AsNoTracking().OrderBy(x => x.RoutingRole).ThenBy(x => x.FallbackOrder)
                .Select(x => new AIProviderResponse(x.Id, x.DisplayName, x.ProviderType, x.BaseUrl, x.Model, x.IsActive,
                    x.RoutingRole, x.FallbackOrder, x.TimeoutSeconds, x.MaxOutputTokens, x.EncryptedApiKey != null,
                    x.CreatedAt, x.UpdatedAt)).ToArrayAsync(ct);
            return Results.Ok(rows);
        });
        group.MapPost("/", async (AIProviderWriteRequest request, CreatorFlowDbContext? db, ITenantContext tenant,
            IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var error = Validate(request);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["provider"] = [error] });
            if (string.IsNullOrWhiteSpace(request.ApiKey))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["apiKey"] = ["API key is required when creating a provider."] });
            var row = new AIProviderConfiguration
            {
                DisplayName = request.DisplayName.Trim(), ProviderType = "openai-compatible",
                BaseUrl = request.BaseUrl.Trim().TrimEnd('/'), Model = request.Model.Trim(),
                EncryptedApiKey = protection.CreateProtector("CreatorFlow.AIProvider.ApiKey.v1").Protect(request.ApiKey),
                IsActive = request.IsActive, RoutingRole = request.RoutingRole,
                FallbackOrder = request.RoutingRole == "Primary" ? null : request.FallbackOrder,
                TimeoutSeconds = request.TimeoutSeconds, MaxOutputTokens = request.MaxOutputTokens
            };
            if (row.RoutingRole == "Primary")
            {
                var current = await db.AIProviderConfigurations.Where(x => x.RoutingRole == "Primary" && x.IsActive).ToListAsync(ct);
                foreach (var item in current) { item.IsActive = false; item.UpdatedAt = DateTimeOffset.UtcNow; }
            }
            db.AIProviderConfigurations.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Primary/fallback ordering conflicts with another provider. Refresh and try again." }); }
            return Results.Created($"/api/v1/ai/providers/{row.Id}", new AIProviderResponse(row.Id, row.DisplayName, row.ProviderType,
                row.BaseUrl, row.Model, row.IsActive, row.RoutingRole, row.FallbackOrder, row.TimeoutSeconds,
                row.MaxOutputTokens, true, row.CreatedAt, row.UpdatedAt));
        });
        group.MapPut("/{id:guid}", async (Guid id, AIProviderWriteRequest request, CreatorFlowDbContext? db, ITenantContext tenant,
            IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var error = Validate(request);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["provider"] = [error] });
            var row = await db.AIProviderConfigurations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.DisplayName = request.DisplayName.Trim(); row.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
            row.Model = request.Model.Trim(); row.IsActive = request.IsActive; row.RoutingRole = request.RoutingRole;
            row.FallbackOrder = request.RoutingRole == "Primary" ? null : request.FallbackOrder;
            row.TimeoutSeconds = request.TimeoutSeconds; row.MaxOutputTokens = request.MaxOutputTokens;
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                row.EncryptedApiKey = protection.CreateProtector("CreatorFlow.AIProvider.ApiKey.v1").Protect(request.ApiKey);
            if (row.RoutingRole == "Primary" && row.IsActive)
            {
                var current = await db.AIProviderConfigurations.Where(x => x.Id != id && x.RoutingRole == "Primary" && x.IsActive).ToListAsync(ct);
                foreach (var item in current) { item.IsActive = false; item.UpdatedAt = DateTimeOffset.UtcNow; }
            }
            row.UpdatedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Primary/fallback ordering conflicts with another provider. Refresh and try again." }); }
            return Results.Ok(new AIProviderResponse(row.Id, row.DisplayName, row.ProviderType, row.BaseUrl, row.Model,
                row.IsActive, row.RoutingRole, row.FallbackOrder, row.TimeoutSeconds, row.MaxOutputTokens,
                row.EncryptedApiKey is not null, row.CreatedAt, row.UpdatedAt));
        });
        group.MapDelete("/{id:guid}", async (Guid id, CreatorFlowDbContext? db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var row = await db.AIProviderConfigurations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            db.AIProviderConfigurations.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPost("/{id:guid}/test", async (Guid id, CreatorFlowDbContext? db, ITenantContext tenant,
            IDataProtectionProvider protection, IHttpClientFactory clients, CancellationToken ct) =>
        {
            if (!CanManage(tenant)) return Results.Forbid();
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var row = await db.AIProviderConfigurations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(row.EncryptedApiKey)) return Results.Conflict(new { success = false, message = "Chưa có API key." });
            try
            {
                var key = protection.CreateProtector("CreatorFlow.AIProvider.ApiKey.v1").Unprotect(row.EncryptedApiKey);
                using var request = new HttpRequestMessage(HttpMethod.Get, row.BaseUrl.TrimEnd('/') + "/models");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await clients.CreateClient().SendAsync(request, ct);
                return Results.Ok(new { success = response.IsSuccessStatusCode, statusCode = (int)response.StatusCode,
                    message = response.IsSuccessStatusCode ? "Nhà cung cấp đã phản hồi xác thực." :
                    $"Nhà cung cấp trả về HTTP {(int)response.StatusCode}. Không ghi nhận nội dung có thể chứa dữ liệu nhạy cảm." });
            }
            catch (Exception) { return Results.Ok(new { success = false, statusCode = (int?)null, message = "Không thể kết nối hoặc giải mã cấu hình. Kiểm tra URL và khóa bảo vệ trên máy chủ." }); }
        });
        return endpoints;
    }

    private static bool CanManage(ITenantContext tenant) => tenant.IsResolved &&
        (string.Equals(tenant.Role, "Owner", StringComparison.Ordinal) || string.Equals(tenant.Role, "Admin", StringComparison.Ordinal));

    private static string? Validate(AIProviderWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 120) return "Tên nhà cung cấp bắt buộc, tối đa 120 ký tự.";
        if (string.IsNullOrWhiteSpace(request.Model) || request.Model.Trim().Length > 160) return "Model bắt buộc, tối đa 160 ký tự.";
        if (request.RoutingRole is not ("Primary" or "Fallback")) return "Vai trò phải là Primary hoặc Fallback.";
        if (request.RoutingRole == "Fallback" && request.FallbackOrder is not (>= 1 and <= 20)) return "Thứ tự dự phòng phải từ 1 đến 20.";
        if (request.TimeoutSeconds is < 5 or > 300) return "Timeout phải từ 5 đến 300 giây.";
        if (request.MaxOutputTokens is < 256 or > 16000) return "Max output tokens phải từ 256 đến 16000.";
        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && (uri.IsLoopback || uri.Host == "host.docker.internal"))))
            return "Base URL phải là HTTPS; HTTP chỉ được phép cho dịch vụ local.";
        return null;
    }
}

public sealed record RuntimeAIProvider(string Provider, string? Model, string? BaseUrl, bool HasApiKey, bool IsPrimary, bool IsFallback);

public sealed record AIProviderWriteRequest(string DisplayName, string BaseUrl, string Model, string? ApiKey,
    bool IsActive, string RoutingRole, int? FallbackOrder, int TimeoutSeconds = 120, int MaxOutputTokens = 3000);
public sealed record AIProviderResponse(Guid Id, string DisplayName, string ProviderType, string BaseUrl, string Model,
    bool IsActive, string RoutingRole, int? FallbackOrder, int TimeoutSeconds, int MaxOutputTokens, bool HasApiKey,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
