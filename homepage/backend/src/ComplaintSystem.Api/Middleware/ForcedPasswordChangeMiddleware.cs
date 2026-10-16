using System.Security.Claims;
using ComplaintSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace ComplaintSystem.Api.Middleware;

public sealed class ForcedPasswordChangeMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/me",
        "/api/auth/csrf",
        "/api/auth/change-password",
        "/api/auth/logout"
    };

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            !AllowedPaths.Contains(context.Request.Path.Value ?? string.Empty) &&
            Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user?.ForcePasswordReset == true)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    code = "password_change_required",
                    message = "Change the temporary password before accessing the dashboard."
                });
                return;
            }
        }

        await next(context);
    }
}
