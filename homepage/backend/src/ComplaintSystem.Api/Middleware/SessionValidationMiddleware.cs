using System.Security.Claims;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace ComplaintSystem.Api.Middleware;

public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISessionService sessionService)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionId = context.User.FindFirstValue(ClaimNames.SessionId);
            if (string.IsNullOrWhiteSpace(sessionId) || !await sessionService.IsActiveAsync(sessionId, context.RequestAborted))
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "session_expired", message = "Your session has expired." });
                return;
            }
            await sessionService.TouchAsync(sessionId, context.RequestAborted);
        }
        await next(context);
    }
}
