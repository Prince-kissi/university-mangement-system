using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ComplaintSystem.Infrastructure.Workers;

public sealed class OverdueEscalationWorker(IServiceScopeFactory scopeFactory, ILogger<OverdueEscalationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await EscalateOverdueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The overdue complaint escalation cycle failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EscalateOverdueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var overdue = await dbContext.Complaints
            .Where(x => x.ResolutionDueAt < now && x.Status != ComplaintStatus.Escalated &&
                x.Status != ComplaintStatus.Resolved && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Rejected)
            .ToListAsync(cancellationToken);
        foreach (var complaint in overdue)
        {
            var previous = complaint.Status;
            complaint.Status = ComplaintStatus.Escalated;
            complaint.EscalationLevel++;
            dbContext.ComplaintStatusHistory.Add(new ComplaintStatusHistory
            {
                ComplaintId = complaint.Id,
                PreviousStatus = previous,
                NewStatus = ComplaintStatus.Escalated,
                Reason = "Automatically escalated after the resolution deadline passed."
            });
        }
        if (overdue.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Automatically escalated {ComplaintCount} overdue complaints.", overdue.Count);
        }
    }
}
