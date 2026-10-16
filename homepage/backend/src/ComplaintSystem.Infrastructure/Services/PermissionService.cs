using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class PermissionService(ApplicationDbContext dbContext) : IPermissionService
{
    public Task<bool> UserHasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (from userRole in dbContext.UserRoles
         join rolePermission in dbContext.RolePermissions on userRole.RoleId equals rolePermission.RoleId
         where userRole.UserId == userId && rolePermission.Permission == permission
         select rolePermission.Id).AnyAsync(cancellationToken);
}
