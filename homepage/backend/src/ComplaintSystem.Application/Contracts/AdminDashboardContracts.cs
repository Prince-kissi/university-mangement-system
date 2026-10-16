using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Application.Contracts;

public sealed record AdminDashboardQuery(
    string? Search = null, ComplaintStatus? Status = null, PriorityLevel? Priority = null,
    string Sort = "newest", int Page = 1, int PageSize = 10);

public sealed record AdminDashboardCountsDto(
    int TotalComplaints, int PendingComplaints, int UnderReview, int InProgress,
    int Resolved, int Rejected, int Closed, int AnonymousComplaints,
    int TodayComplaints, int ThisWeekComplaints, int ThisMonthComplaints,
    int TotalStudents, int TotalDepartmentOfficers, int TotalAdministrators, int TotalDepartments,
    int TotalSuperAdministrators, int ActiveUsers, int SuspendedUsers,
    int SuccessfulLoginsToday, int FailedLoginsToday, int SecurityAlerts);

public sealed record AdminComplaintRowDto(
    Guid Id, string ComplaintId, string? ReferenceNumber, string StudentName, string Title,
    string Department, string Category, PriorityLevel Priority, ComplaintStatus Status,
    DateTimeOffset DateSubmitted, string? AssignedOfficer, DateTimeOffset LastUpdated);

public sealed record AdminChartItemDto(string Label, decimal Value);
public sealed record AdminActivityDto(string Action, string Description, DateTimeOffset Timestamp);

public sealed record AdminDashboardAnalyticsDto(
    IReadOnlyCollection<AdminChartItemDto> ComplaintsByDepartment,
    IReadOnlyCollection<AdminChartItemDto> ComplaintsByStatus,
    IReadOnlyCollection<AdminChartItemDto> MonthlyComplaintTrend,
    IReadOnlyCollection<AdminChartItemDto> ComplaintCategories,
    decimal ResolutionRate, decimal? AverageResolutionHours);

public sealed record AdminDashboardDto(
    CurrentUserDto Administrator, AdminDashboardCountsDto Counts,
    PagedResult<AdminComplaintRowDto> Complaints, AdminDashboardAnalyticsDto Analytics,
    IReadOnlyCollection<AdminActivityDto> RecentActivity,
    IReadOnlyCollection<NotificationDto> Notifications, DateTimeOffset GeneratedAt);
