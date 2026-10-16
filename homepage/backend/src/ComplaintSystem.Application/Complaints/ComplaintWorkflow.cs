using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Application.Complaints;

public static class ComplaintWorkflow
{
    private static readonly IReadOnlyDictionary<ComplaintStatus, ComplaintStatus[]> Allowed =
        new Dictionary<ComplaintStatus, ComplaintStatus[]>
        {
            [ComplaintStatus.Submitted] = [ComplaintStatus.Assigned, ComplaintStatus.Acknowledged, ComplaintStatus.Rejected],
            [ComplaintStatus.Assigned] = [ComplaintStatus.Acknowledged, ComplaintStatus.UnderReview, ComplaintStatus.Escalated],
            [ComplaintStatus.Acknowledged] = [ComplaintStatus.UnderReview, ComplaintStatus.AwaitingStudentResponse,
                ComplaintStatus.InvestigationInProgress, ComplaintStatus.Resolved, ComplaintStatus.Rejected, ComplaintStatus.Escalated],
            [ComplaintStatus.UnderReview] = [ComplaintStatus.AwaitingStudentResponse, ComplaintStatus.InvestigationInProgress,
                ComplaintStatus.Resolved, ComplaintStatus.Rejected, ComplaintStatus.Escalated],
            [ComplaintStatus.AwaitingStudentResponse] = [ComplaintStatus.UnderReview, ComplaintStatus.InvestigationInProgress,
                ComplaintStatus.Resolved, ComplaintStatus.Escalated],
            [ComplaintStatus.InvestigationInProgress] = [ComplaintStatus.AwaitingStudentResponse, ComplaintStatus.Resolved,
                ComplaintStatus.Rejected, ComplaintStatus.Escalated],
            [ComplaintStatus.Escalated] = [ComplaintStatus.UnderReview, ComplaintStatus.InvestigationInProgress,
                ComplaintStatus.Resolved, ComplaintStatus.Rejected],
            [ComplaintStatus.Resolved] = [ComplaintStatus.Closed, ComplaintStatus.Reopened],
            [ComplaintStatus.Closed] = [ComplaintStatus.Reopened],
            [ComplaintStatus.Rejected] = [ComplaintStatus.Reopened],
            [ComplaintStatus.Reopened] = [ComplaintStatus.Assigned, ComplaintStatus.UnderReview,
                ComplaintStatus.InvestigationInProgress, ComplaintStatus.Escalated]
        };

    public static bool CanTransition(ComplaintStatus from, ComplaintStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static (TimeSpan FirstResponse, TimeSpan Resolution) Deadlines(PriorityLevel priority) => priority switch
    {
        PriorityLevel.Low => (TimeSpan.FromHours(72), TimeSpan.FromDays(30)),
        PriorityLevel.Normal => (TimeSpan.FromHours(48), TimeSpan.FromDays(21)),
        PriorityLevel.High => (TimeSpan.FromHours(24), TimeSpan.FromDays(14)),
        PriorityLevel.Urgent => (TimeSpan.FromHours(8), TimeSpan.FromDays(7)),
        PriorityLevel.Critical => (TimeSpan.FromHours(2), TimeSpan.FromDays(2)),
        _ => throw new ArgumentOutOfRangeException(nameof(priority))
    };

    public static bool IsTerminal(ComplaintStatus status) => status is ComplaintStatus.Closed or ComplaintStatus.Rejected;
}
