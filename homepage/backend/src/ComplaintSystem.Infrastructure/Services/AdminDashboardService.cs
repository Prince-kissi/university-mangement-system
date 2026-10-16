using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class AdminDashboardService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IAuthenticationService authenticationService,
    IClock clock) : IAdminDashboardService
{
    public async Task<AdminDashboardDto> GetAsync(Guid administratorId, AdminDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var administrator = await userManager.FindByIdAsync(administratorId.ToString())
            ?? throw new ForbiddenException("Administrator account not found.");
        if (!await userManager.IsInRoleAsync(administrator, AppRoles.Admin) &&
            !await userManager.IsInRoleAsync(administrator, AppRoles.SuperAdmin))
        {
            throw new ForbiddenException("Administrator access is required.");
        }

        var profile = await authenticationService.GetCurrentUserAsync(administratorId, cancellationToken)
            ?? throw new ForbiddenException("Administrator profile not found.");
        var now = clock.UtcNow;
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var month = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var complaints = dbContext.Complaints.AsNoTracking();

        var statusCounts = await complaints.GroupBy(x => x.Status)
            .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        int Status(ComplaintStatus value) => statusCounts.GetValueOrDefault(value);

        var roleCounts = await (from userRole in dbContext.UserRoles.AsNoTracking()
                                join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                join user in dbContext.Users.AsNoTracking() on userRole.UserId equals user.Id
                                where !user.IsDeleted
                                group user by role.Name into grouped
                                select new { Role = grouped.Key!, Count = grouped.Count() })
            .ToDictionaryAsync(x => x.Role, x => x.Count, cancellationToken);

        var total = statusCounts.Values.Sum();
        var counts = new AdminDashboardCountsDto(
            total,
            Status(ComplaintStatus.Submitted),
            Status(ComplaintStatus.UnderReview),
            Status(ComplaintStatus.InvestigationInProgress),
            Status(ComplaintStatus.Resolved),
            Status(ComplaintStatus.Rejected),
            Status(ComplaintStatus.Closed),
            await complaints.CountAsync(x => x.IsAnonymous, cancellationToken),
            await complaints.CountAsync(x => x.CreatedAt >= today, cancellationToken),
            await complaints.CountAsync(x => x.CreatedAt >= week, cancellationToken),
            await complaints.CountAsync(x => x.CreatedAt >= month, cancellationToken),
            roleCounts.GetValueOrDefault(AppRoles.Student),
            roleCounts.GetValueOrDefault(AppRoles.DepartmentOfficer),
            roleCounts.GetValueOrDefault(AppRoles.Admin),
            await dbContext.Departments.AsNoTracking().CountAsync(cancellationToken),
            roleCounts.GetValueOrDefault(AppRoles.SuperAdmin),
            await dbContext.Users.AsNoTracking().CountAsync(x => !x.IsDeleted && x.AccountStatus == AccountStatus.Active, cancellationToken),
            await dbContext.Users.AsNoTracking().CountAsync(x => !x.IsDeleted && x.AccountStatus == AccountStatus.Suspended, cancellationToken),
            await dbContext.AuditLogs.AsNoTracking().CountAsync(x => x.Action == "LoginSuccess" && x.CreatedAt >= today, cancellationToken),
            await dbContext.AuditLogs.AsNoTracking().CountAsync(x => x.Action == "LoginFailure" && x.CreatedAt >= today, cancellationToken),
            await dbContext.AuditLogs.AsNoTracking().CountAsync(x => !x.Succeeded && x.CreatedAt >= week, cancellationToken));

        var filtered = complaints;
        if (query.Status.HasValue) filtered = filtered.Where(x => x.Status == query.Status.Value);
        if (query.Priority.HasValue) filtered = filtered.Where(x => x.Priority == query.Priority.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            filtered = filtered.Where(x => x.PublicId.ToLower().Contains(search) ||
                                           x.Title.ToLower().Contains(search) ||
                                           x.Department.Name.ToLower().Contains(search) ||
                                           x.Category.Name.ToLower().Contains(search) ||
                                           (!x.IsAnonymous && dbContext.Users.Any(u => u.Id == x.StudentUserId &&
                                               ((u.StudentReferenceNumber != null && u.StudentReferenceNumber.Contains(search)) ||
                                                (u.FirstName + " " + u.LastName).ToLower().Contains(search)))));
        }
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var filteredCount = await filtered.CountAsync(cancellationToken);
        var ordered = query.Sort.ToLowerInvariant() switch
        {
            "oldest" => filtered.OrderBy(x => x.CreatedAt),
            "updated" => filtered.OrderByDescending(x => x.UpdatedAt),
            "priority" => filtered.OrderByDescending(x => x.Priority).ThenByDescending(x => x.CreatedAt),
            _ => filtered.OrderByDescending(x => x.CreatedAt)
        };
        var rows = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdminComplaintRowDto(
                x.Id, x.PublicId,
                x.IsAnonymous ? null : dbContext.Users.Where(u => u.Id == x.StudentUserId)
                    .Select(u => u.StudentReferenceNumber).FirstOrDefault(),
                x.IsAnonymous ? "Anonymous" : dbContext.Users.Where(u => u.Id == x.StudentUserId)
                    .Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "Unknown student",
                x.Title, x.Department.Name, x.Category.Name, x.Priority, x.Status, x.CreatedAt,
                (from assignment in dbContext.ComplaintAssignments
                 join officer in dbContext.Users on assignment.OfficerUserId equals officer.Id
                 where assignment.ComplaintId == x.Id && assignment.IsCurrent
                 select officer.FirstName + " " + officer.LastName).FirstOrDefault(),
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        // Keep database aggregation separate from DTO construction. Npgsql can
        // translate the grouped integer count, but not the implicit conversion to
        // the decimal Value property inside AdminChartItemDto.
        var byDepartmentCounts = await complaints.GroupBy(x => x.Department.Name)
            .Select(x => new { Label = x.Key, Value = x.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync(cancellationToken);
        var byDepartment = byDepartmentCounts
            .Select(x => new AdminChartItemDto(x.Label, x.Value))
            .ToList();
        var byStatus = statusCounts.OrderByDescending(x => x.Value)
            .Select(x => new AdminChartItemDto(x.Key.ToString(), x.Value)).ToList();
        var byCategoryCounts = await complaints.GroupBy(x => x.Category.Name)
            .Select(x => new { Label = x.Key, Value = x.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync(cancellationToken);
        var byCategory = byCategoryCounts
            .Select(x => new AdminChartItemDto(x.Label, x.Value))
            .ToList();
        var trendStart = month.AddMonths(-11);
        var monthlyRaw = await complaints.Where(x => x.CreatedAt >= trendStart)
            .GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month })
            .Select(x => new { x.Key.Year, x.Key.Month, Count = x.Count() }).ToListAsync(cancellationToken);
        var trend = Enumerable.Range(0, 12).Select(offset => trendStart.AddMonths(offset))
            .Select(period => new AdminChartItemDto(period.ToString("MMM yyyy"),
                monthlyRaw.FirstOrDefault(x => x.Year == period.Year && x.Month == period.Month)?.Count ?? 0)).ToList();
        var resolutionRate = total == 0 ? 0 : Math.Round((decimal)(Status(ComplaintStatus.Resolved) + Status(ComplaintStatus.Closed)) * 100 / total, 1);
        var resolutionDurations = await complaints.Where(x => x.ResolvedAt != null)
            .Select(x => new { x.CreatedAt, x.ResolvedAt }).ToListAsync(cancellationToken);
        decimal? averageHours = resolutionDurations.Count == 0 ? null :
            Math.Round((decimal)resolutionDurations.Average(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalHours), 1);
        var analytics = new AdminDashboardAnalyticsDto(byDepartment, byStatus, trend, byCategory,
            resolutionRate, averageHours);

        var complaintActivity = await complaints.OrderByDescending(x => x.CreatedAt).Take(10)
            .Select(x => new AdminActivityDto("Complaint submitted", $"{x.PublicId}: {x.Title}", x.CreatedAt))
            .ToListAsync(cancellationToken);
        var auditActivity = await dbContext.AuditLogs.AsNoTracking().Where(x => x.Succeeded)
            .OrderByDescending(x => x.CreatedAt).Take(20)
            .Select(x => new AdminActivityDto(x.Action, x.RelatedRecordType + (x.RelatedRecordId == null ? "" : $" {x.RelatedRecordId}"), x.CreatedAt))
            .ToListAsync(cancellationToken);
        var activity = complaintActivity.Concat(auditActivity).OrderByDescending(x => x.Timestamp).Take(15).ToList();
        var notifications = await dbContext.Notifications.AsNoTracking()
            .Where(x => x.UserId == administratorId).OrderByDescending(x => x.CreatedAt).Take(20)
            .Select(x => new NotificationDto(x.Id, x.Title, x.Message, x.Type, x.Link, x.IsRead, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AdminDashboardDto(profile, counts,
            new PagedResult<AdminComplaintRowDto>(rows, page, pageSize, filteredCount), analytics,
            activity, notifications, now);
    }
}
