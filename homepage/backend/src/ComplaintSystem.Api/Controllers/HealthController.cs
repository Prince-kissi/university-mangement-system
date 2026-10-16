using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ComplaintSystem.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api")]
public sealed class HealthController(
    ApplicationDbContext database,
    IDistributedCache cache,
    ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet("health")]
    public async Task<IActionResult> Overall(CancellationToken cancellationToken)
    {
        var databaseHealthy = await CanConnectToDatabase(cancellationToken);
        var authenticationHealthy = databaseHealthy && await CanConnectToAuthenticationDependencies(cancellationToken);
        var payload = new
        {
            status = databaseHealthy && authenticationHealthy ? "healthy" : "unhealthy",
            services = new
            {
                backend = "healthy",
                database = databaseHealthy ? "healthy" : "unhealthy",
                authentication = authenticationHealthy ? "healthy" : "unhealthy"
            }
        };

        return databaseHealthy && authenticationHealthy
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }

    [HttpGet("database/health")]
    public async Task<IActionResult> Database(CancellationToken cancellationToken)
    {
        var healthy = await CanConnectToDatabase(cancellationToken);
        return healthy
            ? Ok(new { status = "healthy", service = "database" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "unhealthy", service = "database", detail = "Database connection failed." });
    }

    [HttpGet("auth/health")]
    public async Task<IActionResult> Authentication(CancellationToken cancellationToken)
    {
        var databaseHealthy = await CanConnectToDatabase(cancellationToken);
        var healthy = databaseHealthy && await CanConnectToAuthenticationDependencies(cancellationToken);
        return healthy
            ? Ok(new { status = "healthy", service = "authentication" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "unhealthy", service = "authentication", detail = "Authentication service is temporarily unavailable." });
    }

    private async Task<bool> CanConnectToDatabase(CancellationToken cancellationToken)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Database health check failed");
            return false;
        }
    }

    private async Task<bool> CanConnectToAuthenticationDependencies(CancellationToken cancellationToken)
    {
        try
        {
            await cache.GetStringAsync("health:authentication", cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Authentication dependency health check failed");
            return false;
        }
    }
}
