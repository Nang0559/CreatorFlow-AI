using CreatorFlow.Api.Identity;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class TenantRoleGuardMiddlewareTests
{
    [Theory]
    [InlineData("Owner", "/api/v1/content-projects", true)]
    [InlineData("Admin", "/api/v1/content-projects", true)]
    [InlineData("Editor", "/api/v1/topics", true)]
    [InlineData("Editor", "/api/v1/video/render-jobs", true)]
    [InlineData("Editor", "/api/v1/ai/providers", false)]
    [InlineData("Editor", "/api/v1/identity/workspaces/00000000-0000-0000-0000-000000000001/invitations", false)]
    [InlineData("Analyst", "/api/v1/content-projects", false)]
    [InlineData("Publisher", "/api/v1/content-projects", false)]
    [InlineData("Publisher", "/api/v1/publishing/posts", true)]
    [InlineData("Unknown", "/api/v1/content-projects", false)]
    public async Task Mutation_access_follows_role_matrix(string role, string path, bool expectedAllowed)
    {
        var (statusCode, nextCalled) = await InvokeAsync(role, HttpMethods.Post, path);

        Assert.Equal(expectedAllowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, statusCode);
        Assert.Equal(expectedAllowed, nextCalled);
    }

    [Theory]
    [InlineData("Analyst")]
    [InlineData("Publisher")]
    [InlineData("Editor")]
    public async Task Read_requests_are_not_blocked_by_mutation_guard(string role)
    {
        var (statusCode, nextCalled) = await InvokeAsync(role, HttpMethods.Get, "/api/v1/content-projects");

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.True(nextCalled);
    }

    private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(string role, string method, string path)
    {
        var tenant = new TenantContext();
        tenant.Set(Guid.NewGuid(), role);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new TenantRoleGuardMiddleware(_ =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenant);
        return (context.Response.StatusCode, nextCalled);
    }
}
