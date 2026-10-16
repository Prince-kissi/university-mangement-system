using ComplaintSystem.Application.Security;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Api.Middleware;

public sealed class InitialSetupMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        var isStaff = context.User.IsInRole(AppRoles.DepartmentOfficer) ||
                      context.User.IsInRole(AppRoles.Admin) || context.User.IsInRole(AppRoles.SuperAdmin);
        if (isStaff && !context.Request.Path.StartsWithSegments("/api/setup"))
        {
            var configured = await (from userRole in dbContext.UserRoles.AsNoTracking()
                                    join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                    join user in dbContext.Users.AsNoTracking() on userRole.UserId equals user.Id
                                    where role.Name == AppRoles.SuperAdmin && !user.IsDeleted
                                    select user.Id).AnyAsync(context.RequestAborted);
            if (!configured)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Initial Super Administrator setup is required." });
                return;
            }
        }
        await next(context);
    }
}
