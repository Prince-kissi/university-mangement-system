using ComplaintSystem.Application.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var (status, title, detail) = exception switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", exception.Message),
                ForbiddenException => (StatusCodes.Status403Forbidden, "Access denied", exception.Message),
                ConflictException => (StatusCodes.Status409Conflict, "Request conflict", exception.Message),
                AntiforgeryValidationException => (StatusCodes.Status400BadRequest, "Invalid CSRF token", "Refresh the page and try again."),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update", "This record changed while you were editing it. Reload and try again."),
                BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request", "The request could not be processed."),
                _ => (StatusCodes.Status500InternalServerError, "Unexpected server error", "An unexpected error occurred.")
            };
            if (status >= 500)
            {
                logger.LogError(exception, "Unhandled request failure for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                logger.LogWarning("Request rejected with {Status}: {ExceptionType}", status, exception.GetType().Name);
            }
            if (environment.IsDevelopment() && status >= 500)
            {
                detail = exception.Message;
            }
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = context.Request.Path
            });
        }
    }
}
