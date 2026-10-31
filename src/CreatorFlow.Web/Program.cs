var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient<CreatorFlow.Web.Services.CreatorFlowApiClient>(client =>
{
    var baseUrl = builder.Configuration["CreatorFlowApi:BaseUrl"];
    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) client.BaseAddress = uri;
    var apiKey = builder.Configuration["CreatorFlowApi:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", apiKey);
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<CreatorFlow.Web.Components.App>()
    .AddInteractiveServerRenderMode();
app.Run();
