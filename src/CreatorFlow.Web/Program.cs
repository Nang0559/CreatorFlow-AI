using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddTransient<CreatorFlow.Web.Services.OidcAccessTokenHandler>();
builder.Services.AddHttpClient("OidcBackchannel");
builder.Services.AddHttpClient("PublicTikTokResolver", client =>
{
    var baseUrl = builder.Configuration["CreatorFlowApi:BaseUrl"];
    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) client.BaseAddress = uri;
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<CreatorFlow.Web.Services.CreatorFlowApiClient>(client =>
{
    var baseUrl = builder.Configuration["CreatorFlowApi:BaseUrl"];
    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) client.BaseAddress = uri;
    var apiKey = builder.Configuration["CreatorFlowApi:ApiKey"];
    if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", apiKey);
}).AddHttpMessageHandler<CreatorFlow.Web.Services.OidcAccessTokenHandler>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var oidc = builder.Configuration.GetSection("Authentication:Oidc");
var oidcEnabled = !string.IsNullOrWhiteSpace(oidc["Authority"]) &&
                 !string.IsNullOrWhiteSpace(oidc["ClientId"]) &&
                 !string.IsNullOrWhiteSpace(oidc["ClientSecret"]) &&
                 !string.IsNullOrWhiteSpace(oidc["ApiScope"]);
if (!oidcEnabled && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Production Web requires Authentication:Oidc:Authority, ClientId, ClientSecret and ApiScope.");

if (oidcEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-CreatorFlow.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/auth/denied";
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = oidc["Authority"];
        options.ClientId = oidc["ClientId"];
        options.ClientSecret = oidc["ClientSecret"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        // Local Keycloak development uses the authorization-code endpoint directly.
        // Keep PAR available in production, while avoiding the current local PAR rejection.
        options.PushedAuthorizationBehavior = builder.Environment.IsDevelopment()
            ? PushedAuthorizationBehavior.Disable
            : PushedAuthorizationBehavior.UseIfAvailable;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add(oidc["ApiScope"]!);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
            ValidateIssuerSigningKey = true, NameClaimType = "name", RoleClaimType = "role"
        };
        options.Events.OnTokenValidated = context =>
        {
            if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("iss")?.Value) ||
                string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                context.Fail("The identity provider must issue both iss and sub claims.");
            return Task.CompletedTask;
        };
        options.Events.OnTicketReceived = context =>
        {
            if (context.Properties is not null && context.Principal?.Identity is ClaimsIdentity identity)
            {
                AddOrReplaceClaim(identity, "creatorflow_access_token", context.Properties.GetTokenValue("access_token"));
                AddOrReplaceClaim(identity, "creatorflow_refresh_token", context.Properties.GetTokenValue("refresh_token"));
                AddOrReplaceClaim(identity, "creatorflow_access_token_expires_at", context.Properties.GetTokenValue("expires_at"));
            }
            return Task.CompletedTask;
        };
    });
    builder.Services.AddAuthorization(options =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
if (oidcEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/auth/login", (string? returnUrl) =>
    {
        var destination = !string.IsNullOrWhiteSpace(returnUrl) &&
                          returnUrl.StartsWith("/", StringComparison.Ordinal) &&
                          !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : "/";
        return Results.Challenge(new AuthenticationProperties { RedirectUri = destination },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    }).AllowAnonymous();

    app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new { requestToken = tokens.RequestToken });
    }).RequireAuthorization();

    app.MapGet("/auth/session", async (HttpContext context) =>
    {
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        return Results.Ok(new
        {
            authenticated,
            subject = authenticated ? context.User.FindFirst("sub")?.Value : null,
            issuer = authenticated ? context.User.FindFirst("iss")?.Value : null,
            selectedTenantId = authenticated ? context.User.FindFirst("creatorflow_tenant")?.Value : null,
            hasRefreshToken = authenticated && !string.IsNullOrWhiteSpace(await context.GetTokenAsync("refresh_token")),
            accessTokenExpiresAt = authenticated ? await context.GetTokenAsync("expires_at") : null
        });
    }).AllowAnonymous();

    app.MapPost("/auth/select-workspace/{tenantId:guid}", async (Guid tenantId, HttpContext context,
        IAntiforgery antiforgery, CreatorFlow.Web.Services.CreatorFlowApiClient api) =>
    {
        await antiforgery.ValidateRequestAsync(context);
        var workspaces = await api.GetAsync<WorkspaceSelectionItem[]>("/api/v1/identity/workspaces");
        var selected = workspaces?.SingleOrDefault(x => x.Id == tenantId);
        if (selected is null) return Results.Forbid();
        var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (auth.Principal?.Identity is not ClaimsIdentity identity) return Results.Unauthorized();
        foreach (var claim in identity.FindAll("creatorflow_tenant").ToArray()) identity.RemoveClaim(claim);
        identity.AddClaim(new Claim("creatorflow_tenant", selected.Id.ToString("D")));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, auth.Principal, auth.Properties);
        return Results.Ok(new { selected.Id, selected.Name, selected.Role });
    }).RequireAuthorization();

    app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(context);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }).RequireAuthorization();
    app.MapGet("/auth/denied", () => Results.Problem("You do not have access to this resource.", statusCode: 403))
        .AllowAnonymous();
}
app.UseAntiforgery();

// Anonymous, narrowly-scoped BFF route used by the iOS Share Sheet shortcut.
// The API endpoint only resolves public TikTok page metadata; it does not expose API/SQL ports.
app.MapGet("/public-media/tiktok/resolve", async (string? url, IHttpClientFactory clients, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(url))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Vui lòng cung cấp liên kết TikTok."] });
    var client = clients.CreateClient("PublicTikTokResolver");
    var target = "/api/v1/public-media/tiktok/resolve?url=" + Uri.EscapeDataString(url);
    using var response = await client.GetAsync(target, ct);
    var body = await response.Content.ReadAsStringAsync(ct);
    return Results.Content(body, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
}).AllowAnonymous();

app.MapGet("/media/video/{id:guid}", async (Guid id, HttpContext context, CreatorFlow.Web.Services.CreatorFlowApiClient api) =>
{
    var download = context.Request.Query.ContainsKey("download");
    var endpoint = download ? "download" : "preview";
    using var upstream = await api.GetRawResponseAsync($"/api/v1/video/render-jobs/{id}/{endpoint}",
        context.Request.Headers.Range.ToString(), context.RequestAborted);
    context.Response.StatusCode = (int)upstream.StatusCode;
    if (upstream.Content.Headers.ContentType is not null)
        context.Response.ContentType = upstream.Content.Headers.ContentType.ToString();
    if (upstream.Content.Headers.ContentLength.HasValue)
        context.Response.ContentLength = upstream.Content.Headers.ContentLength.Value;
    if (upstream.Headers.AcceptRanges.Count > 0)
        context.Response.Headers.AcceptRanges = string.Join(",", upstream.Headers.AcceptRanges);
    if (upstream.Content.Headers.ContentRange is not null)
        context.Response.Headers.ContentRange = upstream.Content.Headers.ContentRange.ToString();
    if (upstream.Content.Headers.ContentDisposition is not null)
        context.Response.Headers.ContentDisposition = upstream.Content.Headers.ContentDisposition.ToString();
    if (upstream.IsSuccessStatusCode)
        await upstream.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});
var components = app.MapRazorComponents<CreatorFlow.Web.Components.App>().AddInteractiveServerRenderMode();
if (oidcEnabled) components.RequireAuthorization();
else
{
    app.MapGet("/auth/login", () => Results.Problem(
        "Đăng nhập SaaS chưa được cấu hình. Hãy thiết lập Authentication:Oidc tại môi trường triển khai.",
        statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();
}
app.Run();

static void AddOrReplaceClaim(ClaimsIdentity identity, string type, string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return;
    foreach (var claim in identity.FindAll(type).ToArray())
        identity.RemoveClaim(claim);
    identity.AddClaim(new Claim(type, value));
}

public sealed record WorkspaceSelectionItem(Guid Id, string Name, string Slug, string Role);
