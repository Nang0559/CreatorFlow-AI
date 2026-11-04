using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Api.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace CreatorFlow.Api.Tests;

internal sealed class CreatorFlowApiFactory : WebApplicationFactory<Program>
{
    internal const string ApiKey = "integration-test-api-key-creatorflow-000000000000";
    internal static readonly Guid LegacyTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    internal const string TestJwtIssuer = "https://creatorflow-test.invalid/realms/creatorflow";
    private const string TestJwtAudience = "creatorflow-api";
    private const string TestJwtSigningSecret = "creatorflow-integration-test-signing-key-32";
    
    internal static string CreateJwtToken(string subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSigningSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subject), new Claim("name", "Integration Test User")],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private readonly string _databaseName = $"creatorflow-api-integration-{Guid.NewGuid():N}";
    private readonly string _bootstrapRole;

    internal CreatorFlowApiFactory(string bootstrapRole = "Owner") => _bootstrapRole = bootstrapRole;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CreatorFlow"] = "Server=localhost;Database=CreatorFlow_QC;Integrated Security=True;TrustServerCertificate=True",
                ["Authentication:ApiKey"] = ApiKey,
                ["Tenant:AllowApiKeyBootstrap"] = "true",
                ["Tenant:BootstrapTenantId"] = LegacyTenantId.ToString(),
                ["Tenant:BootstrapRole"] = _bootstrapRole,
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CreatorFlowDbContext>>();
            services.RemoveAll<CreatorFlowDbContext>();
            services.AddDbContext<CreatorFlowDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Keep the real JwtBearer handler and token validation path, but use a deterministic
                // test issuer/signing key so the API does not depend on a live Keycloak service.
                options.Authority = null;
                options.MetadataAddress = null;
                options.ConfigurationManager = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestJwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestJwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSigningSecret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });
        });
    }

    internal Task SeedAsync(Func<CreatorFlowDbContext, Task> seed) =>
        SeedAsync((db, _) => seed(db));

    internal async Task SeedAsync(Func<CreatorFlowDbContext, TenantContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(LegacyTenantId, "Owner");
        var db = scope.ServiceProvider.GetRequiredService<CreatorFlowDbContext>();
        if (!await db.Tenants.AnyAsync(x => x.Id == LegacyTenantId))
        {
            var tenant = Tenant.Create("Legacy workspace", "legacy-workspace", DateTimeOffset.UtcNow);
            db.Entry(tenant).Property(x => x.Id).CurrentValue = LegacyTenantId;
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        await seed(db, tenantContext);
        await db.SaveChangesAsync();
    }
}
