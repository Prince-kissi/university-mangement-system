using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ComplaintSystem.Infrastructure.Workers;

public sealed class PendingRegistrationCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingRegistrationCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await DeleteExpiredRegistrationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The pending student registration cleanup cycle failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeleteExpiredRegistrationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var users = await dbContext.Users
            .Where(x => x.AccountStatus == AccountStatus.PendingVerification && !x.IsVerified && x.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var verifications = await dbContext.StudentVerifications
                .Where(x => x.StudentId == user.Id).ToListAsync(cancellationToken);
            var student = await dbContext.Students.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            dbContext.StudentVerifications.RemoveRange(verifications);
            if (student is not null) dbContext.Students.Remove(student);
            await dbContext.SaveChangesAsync(cancellationToken);
            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
                logger.LogWarning("Could not delete expired pending registration {StudentId}: {Errors}",
                    user.Id, string.Join(",", result.Errors.Select(x => x.Code)));
        }

        if (users.Count > 0)
            logger.LogInformation("Deleted {RegistrationCount} unverified student registrations older than 24 hours.", users.Count);
    }
}
