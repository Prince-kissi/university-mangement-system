using System.Security.Claims;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace ComplaintSystem.UnitTests;

public sealed class PermissionAuthorizationTests
{
    [Fact]
    public async Task Succeeds_when_database_permission_is_granted()
    {
        var requirement = new PermissionRequirement("reports.view");
        var context = CreateContext(requirement);
        await new PermissionAuthorizationHandler(new PermissionStub(true)).HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_database_permission_is_missing()
    {
        var requirement = new PermissionRequirement("backups.manage");
        var context = CreateContext(requirement);
        await new PermissionAuthorizationHandler(new PermissionStub(false)).HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(PermissionRequirement requirement)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test");
        return new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), null);
    }

    private sealed class PermissionStub(bool granted) : IPermissionService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(granted);
    }
}
