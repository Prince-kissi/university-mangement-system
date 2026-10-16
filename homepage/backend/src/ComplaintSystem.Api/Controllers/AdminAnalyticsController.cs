using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "ActiveRole:AdminOrSuper")]
[Route("api/admin/analytics")]
public sealed class AdminAnalyticsController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users) : ApiControllerBase
{
    private static readonly TimeZoneInfo GhanaTimeZone = ResolveGhanaTimeZone();

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] AnalyticsQuery filter, CancellationToken ct)
    {
        var actor = await users.FindByIdAsync(CurrentUserId.ToString());
        if (actor is null) return Forbid();
        var isSuper = await users.IsInRoleAsync(actor, AppRoles.SuperAdmin);
        var nowGhana = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GhanaTimeZone);
        var today = new DateTimeOffset(nowGhana.Year, nowGhana.Month, nowGhana.Day, 0, 0, 0, nowGhana.Offset);
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var month = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, today.Offset);
        var year = new DateTimeOffset(today.Year, 1, 1, 0, 0, 0, today.Offset);
        var start = filter.StartDate is null ? year : GhanaBoundary(filter.StartDate.Value);
        var end = filter.EndDate is null ? today.AddDays(1) : GhanaBoundary(filter.EndDate.Value).AddDays(1);

        var complaints = db.Complaints.AsNoTracking().AsQueryable();
        if (!isSuper && actor.DepartmentId.HasValue) complaints = complaints.Where(x => x.DepartmentId == actor.DepartmentId);
        if (filter.AdministrativeUnitId.HasValue) complaints = complaints.Where(x => x.DepartmentId == filter.AdministrativeUnitId);
        if (filter.CategoryId.HasValue) complaints = complaints.Where(x => x.CategoryId == filter.CategoryId);
        if (filter.Status.HasValue) complaints = complaints.Where(x => x.Status == filter.Status);
        if (filter.Priority.HasValue) complaints = complaints.Where(x => x.Priority == filter.Priority);

        var complaintRows = await complaints.Select(x => new
        {
            x.Id, x.DepartmentId, Department = x.Department.Name, x.Status, x.IsAnonymous, x.CreatedAt,
            x.FirstRespondedAt, x.FirstResponseDueAt, x.ResolvedAt
        }).ToListAsync(ct);
        var selectedComplaints = complaintRows.Where(x => x.CreatedAt >= start && x.CreatedAt < end).ToList();
        var complaintIds = complaintRows.Select(x => x.Id).ToHashSet();

        var responseQuery = db.ComplaintResponses.AsNoTracking()
            .Where(x => x.AuthorUserId != null && complaintIds.Contains(x.ComplaintId));
        if (filter.StaffId.HasValue) responseQuery = responseQuery.Where(x => x.AuthorUserId == filter.StaffId);
        var responseRows = await (from response in responseQuery
            join author in db.Users.AsNoTracking() on response.AuthorUserId equals author.Id
            join userRole in db.UserRoles.AsNoTracking() on author.Id equals userRole.UserId
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            join complaint in db.Complaints.AsNoTracking() on response.ComplaintId equals complaint.Id
            where role.Name == AppRoles.DepartmentOfficer || role.Name == AppRoles.Admin || role.Name == AppRoles.SuperAdmin
            select new
            {
                response.Id, response.ComplaintId, response.CreatedAt, response.AuthorUserId,
                Staff = author.FirstName + " " + author.LastName, Role = role.Name!,
                DepartmentId = complaint.DepartmentId, Department = complaint.Department.Name,
                complaint.FirstResponseDueAt, complaint.FirstRespondedAt, complaint.ResolvedAt,
                ComplaintCreatedAt = complaint.CreatedAt, complaint.Status
            }).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(filter.Role))
            responseRows = responseRows.Where(x => RoleKey(x.Role) == RoleKey(filter.Role)).ToList();
        var selectedResponses = responseRows.Where(x => x.CreatedAt >= start && x.CreatedAt < end).ToList();

        var departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        if (!isSuper && actor.DepartmentId.HasValue) departments = departments.Where(x => x.Id == actor.DepartmentId).ToList();

        object Summary<T>(IEnumerable<T> source, Func<T, DateTimeOffset> timestamp)
        {
            var rows = source.ToList();
            return new
            {
                today = rows.Count(x => timestamp(x) >= today && timestamp(x) < today.AddDays(1)),
                week = rows.Count(x => timestamp(x) >= week && timestamp(x) < week.AddDays(7)),
                month = rows.Count(x => timestamp(x) >= month && timestamp(x) < month.AddMonths(1)),
                year = rows.Count(x => timestamp(x) >= year && timestamp(x) < year.AddYears(1))
            };
        }

        object Trends<T>(IEnumerable<T> source, Func<T, DateTimeOffset> timestamp)
        {
            var dates = source.Select(timestamp).Select(Ghana).ToList();
            return new
            {
                daily = Enumerable.Range(0, 24).Select(hour => new { label = $"{hour:00}:00", value = dates.Count(x => x >= today && x < today.AddDays(1) && x.Hour == hour), start = today.AddHours(hour), end = today.AddHours(hour + 1) }),
                weekly = Enumerable.Range(0, 7).Select(day => new { label = week.AddDays(day).ToString("ddd"), value = dates.Count(x => x.Date == week.AddDays(day).Date), start = week.AddDays(day), end = week.AddDays(day + 1) }),
                monthly = Enumerable.Range(0, DateTime.DaysInMonth(today.Year, today.Month)).Select(day => new { label = month.AddDays(day).ToString("dd MMM"), value = dates.Count(x => x.Date == month.AddDays(day).Date), start = month.AddDays(day), end = month.AddDays(day + 1) }),
                yearly = Enumerable.Range(0, 12).Select(m => new { label = year.AddMonths(m).ToString("MMM"), value = dates.Count(x => x.Year == year.Year && x.Month == m + 1), start = year.AddMonths(m), end = year.AddMonths(m + 1) })
            };
        }

        var totalComplaints = selectedComplaints.Count;
        var complaintsByUnit = departments.Select(unit =>
        {
            var rows = selectedComplaints.Where(x => x.DepartmentId == unit.Id).ToList();
            return new
            {
                id = unit.Id, name = unit.Name, total = rows.Count,
                percentage = Percent(rows.Count, totalComplaints),
                @new = rows.Count(x => x.Status == ComplaintStatus.Submitted),
                underReview = rows.Count(x => x.Status == ComplaintStatus.UnderReview),
                inProgress = rows.Count(x => x.Status == ComplaintStatus.InvestigationInProgress),
                resolved = rows.Count(x => x.Status == ComplaintStatus.Resolved),
                closed = rows.Count(x => x.Status == ComplaintStatus.Closed),
                averageResolutionHours = AverageHours(rows.Where(x => x.ResolvedAt.HasValue).Select(x => x.ResolvedAt!.Value - x.CreatedAt))
            };
        }).OrderByDescending(x => x.total).ThenBy(x => x.name).Select((x, index) => new { rank = index + 1, x.id, x.name, x.total, x.percentage, x.@new, x.underReview, x.inProgress, x.resolved, x.closed, x.averageResolutionHours }).ToList();

        var totalResponses = selectedResponses.Count;
        var responsesByRole = new[] { AppRoles.DepartmentOfficer, AppRoles.Admin, AppRoles.SuperAdmin }.Select(role =>
        {
            var rows = selectedResponses.Where(x => x.Role == role).ToList();
            return new { role = RoleLabel(role), total = rows.Count, percentage = Percent(rows.Count, totalResponses), complaintsRespondedTo = rows.Select(x => x.ComplaintId).Distinct().Count(), averageFirstResponseHours = AverageHours(rows.Where(x => x.FirstRespondedAt.HasValue).Select(x => x.FirstRespondedAt!.Value - x.ComplaintCreatedAt)), averageResponsesPerComplaint = rows.Count == 0 ? 0 : Math.Round((decimal)rows.Count / rows.Select(x => x.ComplaintId).Distinct().Count(), 2), withinExpectedPeriod = rows.Count(x => !x.FirstResponseDueAt.HasValue || x.CreatedAt <= x.FirstResponseDueAt), late = rows.Count(x => x.FirstResponseDueAt.HasValue && x.CreatedAt > x.FirstResponseDueAt) };
        }).ToList();

        var responsesByUnit = departments.Select(unit =>
        {
            var rows = selectedResponses.Where(x => x.DepartmentId == unit.Id).ToList();
            var unitComplaints = selectedComplaints.Where(x => x.DepartmentId == unit.Id).ToList();
            var responded = rows.Select(x => x.ComplaintId).Distinct().Count();
            return new { id = unit.Id, name = unit.Name, total = rows.Count, uniqueComplaints = responded, averageFirstResponseHours = AverageHours(rows.Where(x => x.FirstRespondedAt.HasValue).Select(x => x.FirstRespondedAt!.Value - x.ComplaintCreatedAt)), averageResolutionHours = AverageHours(rows.Where(x => x.ResolvedAt.HasValue).Select(x => x.ResolvedAt!.Value - x.ComplaintCreatedAt)), awaitingResponse = unitComplaints.Count(x => !x.FirstRespondedAt.HasValue && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Resolved), responseRate = Percent(responded, unitComplaints.Count), late = rows.Count(x => x.FirstResponseDueAt.HasValue && x.CreatedAt > x.FirstResponseDueAt) };
        }).OrderByDescending(x => x.total).ThenBy(x => x.name).Select((x, index) => new { rank = index + 1, x.id, x.name, x.total, x.uniqueComplaints, x.averageFirstResponseHours, x.averageResolutionHours, x.awaitingResponse, x.responseRate, x.late }).ToList();

        var byStaff = selectedResponses.GroupBy(x => new { x.AuthorUserId, x.Staff, x.Role, x.Department }).Select(group =>
        {
            var handled = group.Select(x => x.ComplaintId).Distinct().ToList();
            return new { id = group.Key.AuthorUserId, name = group.Key.Staff, role = RoleLabel(group.Key.Role), unit = group.Key.Department, total = group.Count(), complaintsHandled = handled.Count, averageFirstResponseHours = AverageHours(group.Where(x => x.FirstRespondedAt.HasValue).Select(x => x.FirstRespondedAt!.Value - x.ComplaintCreatedAt)), resolved = group.Where(x => x.Status is ComplaintStatus.Resolved or ComplaintStatus.Closed).Select(x => x.ComplaintId).Distinct().Count(), pending = group.Where(x => x.Status is not ComplaintStatus.Resolved and not ComplaintStatus.Closed).Select(x => x.ComplaintId).Distinct().Count(), late = group.Count(x => x.FirstResponseDueAt.HasValue && x.CreatedAt > x.FirstResponseDueAt), lastResponse = group.Max(x => x.CreatedAt) };
        }).OrderByDescending(x => x.total).ToList();

        var anonymousComplaints = complaintRows.Where(x => x.IsAnonymous).ToList();
        return Ok(new { timezone = "Africa/Accra", generatedAt = DateTimeOffset.UtcNow, scope = isSuper ? "System-wide" : actor.DepartmentId.HasValue ? "Assigned administrative unit" : "Permitted administrator scope", complaintSummary = Summary(complaintRows, x => x.CreatedAt), complaintTrends = Trends(complaintRows, x => x.CreatedAt), anonymousComplaintSummary = Summary(anonymousComplaints, x => x.CreatedAt), anonymousComplaintTrends = Trends(anonymousComplaints, x => x.CreatedAt), complaintsByUnit, responseSummary = Summary(responseRows, x => x.CreatedAt), responseTrends = Trends(responseRows, x => x.CreatedAt), responsesByRole, responsesByUnit, staffPerformance = byStaff, filters = new { start, end, totalComplaints, anonymousComplaints = selectedComplaints.Count(x => x.IsAnonymous), totalResponses } });
    }

    private static decimal Percent(int value, int total) => total == 0 ? 0 : Math.Round(value * 100m / total, 1);
    private static decimal? AverageHours(IEnumerable<TimeSpan> values) { var rows = values.ToList(); return rows.Count == 0 ? null : Math.Round((decimal)rows.Average(x => x.TotalHours), 1); }
    private static string RoleLabel(string role) => role switch { AppRoles.DepartmentOfficer => "Department Officers", AppRoles.Admin => "Administrators", AppRoles.SuperAdmin => "Super Administrators", _ => role };
    private static string RoleKey(string role) => role.Replace("_", "").Replace(" ", "").ToLowerInvariant().TrimEnd('s');
    private static DateTimeOffset Ghana(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, GhanaTimeZone);
    private static DateTimeOffset GhanaBoundary(DateOnly value) => new(value.Year, value.Month, value.Day, 0, 0, 0, Ghana(DateTimeOffset.UtcNow).Offset);
    private static TimeZoneInfo ResolveGhanaTimeZone() { try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Accra"); } catch { return TimeZoneInfo.FindSystemTimeZoneById("Greenwich Standard Time"); } }
}

public sealed record AnalyticsQuery(DateOnly? StartDate = null, DateOnly? EndDate = null,
    Guid? AdministrativeUnitId = null, Guid? CategoryId = null, ComplaintStatus? Status = null,
    PriorityLevel? Priority = null, string? Role = null, Guid? StaffId = null);
