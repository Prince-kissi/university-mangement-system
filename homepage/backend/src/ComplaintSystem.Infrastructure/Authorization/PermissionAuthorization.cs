using System.Security.Claims;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Authorization;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var activeRole = context.User.FindFirstValue(ClaimNames.ActiveRole);
        var activeRoleAllows = activeRole is not null && Permissions.Defaults.TryGetValue(activeRole, out var activePermissions) &&
            activePermissions.Contains(requirement.Permission, StringComparer.Ordinal);
        if ((activeRole is not null && activeRoleAllows) ||
            (activeRole is null && Guid.TryParse(subject, out var userId) &&
             await permissionService.UserHasPermissionAsync(userId, requirement.Permission, CancellationToken.None)))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        const string prefix = "Permission:";
        if (!policyName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return base.GetPolicyAsync(policyName);
        }

        var permission = policyName[prefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
