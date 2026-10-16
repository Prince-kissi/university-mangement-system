using System.Security.Cryptography;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Complaints;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Options;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class ComplaintService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IAnonymousTrackingService trackingService,
    IPermissionService permissionService,
    INotificationService notificationService,
    IAuditService auditService,
    IClock clock,
    IOptions<SecurityOptions> securityOptions) : IComplaintService
{
    private readonly SecurityOptions _security = securityOptions.Value;

    public async Task<Result<ComplaintCreatedResponse>> SubmitAsync(Guid studentId, SubmitComplaintRequest request,
        CancellationToken cancellationToken)
    {
        var student = await userManager.FindByIdAsync(studentId.ToString());
        if (student is null || !await userManager.IsInRoleAsync(student, AppRoles.Student))
        {
            throw new ForbiddenException("Only students can submit identified complaints.");
        }
        var category = await GetActiveCategoryAsync(request.CategoryId, request.DepartmentId, cancellationToken);
        var complaint = await CreateComplaintAsync(studentId, request.Title, request.Description, category,
            request.DepartmentId, false, cancellationToken);
        return Result<ComplaintCreatedResponse>.Success(new ComplaintCreatedResponse(complaint.PublicId, null));
    }

    public async Task<Result<ComplaintCreatedResponse>> SubmitAnonymousAsync(SubmitAnonymousComplaintRequest request,
        CancellationToken cancellationToken)
    {
        var anonymousEnabled = await dbContext.SystemSettings.AsNoTracking()
            .Where(x => x.Key == "AnonymousComplaints.Enabled")
            .Select(x => x.Value).SingleOrDefaultAsync(cancellationToken);
        if (!bool.TryParse(anonymousEnabled, out var enabled) || !enabled)
        {
            return Result<ComplaintCreatedResponse>.Failure("anonymous_disabled", "Anonymous complaints are currently unavailable.");
        }
        var category = await GetActiveCategoryAsync(request.CategoryId, request.DepartmentId, cancellationToken);
        if (!category.AllowsAnonymous)
        {
            return Result<ComplaintCreatedResponse>.Failure("category_not_anonymous", "This category does not accept anonymous complaints.");
        }
        var complaint = await CreateComplaintAsync(null, request.Title, request.Description, category,
            request.DepartmentId, true, cancellationToken);
        var rawCode = trackingService.GenerateCode();
        dbContext.AnonymousTrackingRecords.Add(new AnonymousTrackingRecord
        {
            ComplaintId = complaint.Id,
            TrackingCodeHash = trackingService.HashCode(rawCode)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<ComplaintCreatedResponse>.Success(new ComplaintCreatedResponse(complaint.PublicId, rawCode));
    }

    public async Task<Result<ComplaintDetailsDto>> TrackAnonymousAsync(AnonymousTrackRequest request,
        CancellationToken cancellationToken)
    {
        var tracking = await dbContext.AnonymousTrackingRecords
            .Include(x => x.Complaint).ThenInclude(x => x.Category)
            .Include(x => x.Complaint).ThenInclude(x => x.Department)
            .Include(x => x.Complaint).ThenInclude(x => x.StatusHistory)
            .Include(x => x.Complaint).ThenInclude(x => x.Responses)
            .Include(x => x.Complaint).ThenInclude(x => x.EvidenceFiles)
            .SingleOrDefaultAsync(x => x.Complaint.PublicId == request.ComplaintId && x.Complaint.IsAnonymous, cancellationToken);
        if (tracking is null)
        {
            await Task.Delay(150, cancellationToken);
            return Result<ComplaintDetailsDto>.Failure("tracking_failed", "The complaint ID or tracking code is incorrect.");
        }
        if (tracking.LockedUntil > clock.UtcNow)
        {
            return Result<ComplaintDetailsDto>.Failure("tracking_locked", "Tracking is temporarily locked after repeated failures.");
        }
        if (!trackingService.Verify(request.TrackingCode, tracking.TrackingCodeHash))
        {
            tracking.FailedAttempts++;
            if (tracking.FailedAttempts >= _security.AnonymousTrackingMaxFailures)
            {
                tracking.LockedUntil = clock.UtcNow.AddMinutes(_security.AnonymousTrackingLockoutMinutes);
                tracking.FailedAttempts = 0;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<ComplaintDetailsDto>.Failure("tracking_failed", "The complaint ID or tracking code is incorrect.");
        }
        tracking.FailedAttempts = 0;
        tracking.LockedUntil = null;
        tracking.LastUsedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<ComplaintDetailsDto>.Success(ToDetails(tracking.Complaint));
    }

    public async Task<PagedResult<ComplaintSummaryDto>> GetAsync(Guid userId, ComplaintQuery query,
        CancellationToken cancellationToken)
    {
        var accessible = await AccessibleComplaintsAsync(userId, cancellationToken);
        if (query.Status.HasValue) accessible = accessible.Where(x => x.Status == query.Status.Value);
        if (query.Priority.HasValue) accessible = accessible.Where(x => x.Priority == query.Priority.Value);
        if (query.DepartmentId.HasValue) accessible = accessible.Where(x => x.DepartmentId == query.DepartmentId.Value);
        if (query.CategoryId.HasValue) accessible = accessible.Where(x => x.CategoryId == query.CategoryId.Value);
        if (query.AssignedToMe) accessible = accessible.Where(x => x.Assignments.Any(a => a.IsCurrent && a.OfficerUserId == userId));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            accessible = accessible.Where(x => x.PublicId.ToLower().Contains(search) || x.Title.ToLower().Contains(search));
        }
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var total = await accessible.CountAsync(cancellationToken);
        var items = await accessible.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ComplaintSummaryDto(x.Id, x.PublicId, x.Title, x.Category.Name, x.Department.Name,
                x.Priority, x.Status, x.IsAnonymous, x.CreatedAt, x.ResolutionDueAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<ComplaintSummaryDto>(items, page, pageSize, total);
    }

    public async Task<ComplaintDetailsDto> GetByIdAsync(Guid userId, Guid complaintId, CancellationToken cancellationToken)
    {
        var complaint = await (await AccessibleComplaintsAsync(userId, cancellationToken))
            .Include(x => x.Category).Include(x => x.Department).Include(x => x.StatusHistory)
            .Include(x => x.Responses).Include(x => x.EvidenceFiles)
            .SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken);
        return complaint is null ? throw new NotFoundException("Complaint not found.") : ToDetails(complaint);
    }

    public async Task<Result> AssignAsync(Guid actorId, Guid complaintId, AssignComplaintRequest request,
        CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.Include(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var officer = await userManager.FindByIdAsync(request.OfficerUserId.ToString())
            ?? throw new NotFoundException("Officer not found.");
        if (!await userManager.IsInRoleAsync(officer, AppRoles.DepartmentOfficer) ||
            officer.DepartmentId != complaint.DepartmentId || officer.AccountStatus != AccountStatus.Active)
        {
            return Result.Failure("invalid_assignment", "The selected officer must be active and belong to the complaint department.");
        }
        foreach (var current in complaint.Assignments.Where(x => x.IsCurrent))
        {
            current.IsCurrent = false;
            current.UnassignedAt = clock.UtcNow;
        }
        complaint.Assignments.Add(new ComplaintAssignment
        {
            ComplaintId = complaint.Id,
            OfficerUserId = officer.Id,
            AssignedByUserId = actorId,
            AssignedAt = clock.UtcNow,
            Reason = request.Reason.Trim(),
            IsCurrent = true
        });
        var previous = complaint.Status;
        if (complaint.Status is ComplaintStatus.Submitted or ComplaintStatus.Reopened)
        {
            complaint.Status = ComplaintStatus.Assigned;
            complaint.StatusHistory.Add(new ComplaintStatusHistory
            {
                PreviousStatus = previous, NewStatus = complaint.Status, ChangedByUserId = actorId,
                Reason = "Complaint assigned to a department officer."
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService.SendToUserAsync(officer.Id, "Complaint assigned",
            $"Complaint {complaint.PublicId} has been assigned to you.", NotificationType.ComplaintUpdate,
            $"/dashboards/department-officer/complaint-details.html?id={complaint.Id}", cancellationToken);
        await auditService.WriteAsync(actorId, "ComplaintAssigned", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new { officerId = officer.Id, request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(Guid actorId, Guid complaintId, ChangeComplaintStatusRequest request,
        CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        var isStudent = await userManager.IsInRoleAsync(actor, AppRoles.Student);
        if (isStudent)
        {
            if (complaint.StudentUserId != actorId || request.Status != ComplaintStatus.Reopened ||
                complaint.Status is not (ComplaintStatus.Resolved or ComplaintStatus.Closed or ComplaintStatus.Rejected))
            {
                throw new ForbiddenException("Students may only reopen their own resolved, closed or rejected complaints.");
            }
        }
        else
        {
            await EnsureStaffDepartmentAccessAsync(actor, complaint, cancellationToken);
        }
        if (!ComplaintWorkflow.CanTransition(complaint.Status, request.Status))
        {
            return Result.Failure("invalid_status_transition", $"A complaint cannot move from {complaint.Status} to {request.Status}.");
        }
        var previous = complaint.Status;
        complaint.Status = request.Status;
        if (request.Status == ComplaintStatus.Resolved) complaint.ResolvedAt = clock.UtcNow;
        if (request.Status == ComplaintStatus.Closed) complaint.ClosedAt = clock.UtcNow;
        if (request.Status == ComplaintStatus.Reopened)
        {
            complaint.ResolvedAt = null;
            complaint.ClosedAt = null;
        }
        if (request.Status == ComplaintStatus.Escalated) complaint.EscalationLevel++;
        complaint.StatusHistory.Add(new ComplaintStatusHistory
        {
            ComplaintId = complaint.Id, PreviousStatus = previous, NewStatus = request.Status,
            ChangedByUserId = actorId, Reason = request.Reason.Trim()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (complaint.StudentUserId.HasValue)
        {
            await notificationService.SendToUserAsync(complaint.StudentUserId.Value, "Complaint status updated",
                $"Complaint {complaint.PublicId} is now {request.Status}.", NotificationType.ComplaintUpdate,
                $"/student-dashboard.html?complaint={complaint.Id}", cancellationToken);
        }
        await auditService.WriteAsync(actorId, "ComplaintStatusChanged", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new { previous, current = request.Status, request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddResponseAsync(Guid actorId, Guid complaintId, AddResponseRequest request,
        CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        var isStudent = await userManager.IsInRoleAsync(actor, AppRoles.Student);
        if (isStudent && complaint.StudentUserId != actorId)
        {
            throw new ForbiddenException("You cannot respond to another student's complaint.");
        }
        if (!isStudent)
        {
            await EnsureStaffDepartmentAccessAsync(actor, complaint, cancellationToken);
            complaint.FirstRespondedAt ??= clock.UtcNow;
        }
        dbContext.ComplaintResponses.Add(new ComplaintResponse
        {
            ComplaintId = complaint.Id, AuthorUserId = actorId, Body = request.Body.Trim(),
            Visibility = ResponseVisibility.Public
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (!isStudent && complaint.StudentUserId.HasValue)
        {
            await notificationService.SendToUserAsync(complaint.StudentUserId.Value, "New response to your complaint",
                $"A staff member responded to complaint {complaint.PublicId}.", NotificationType.ComplaintUpdate,
                $"/track-complaint.html?id={complaint.Id}", cancellationToken);
        }
        await auditService.WriteAsync(actorId, "ComplaintResponseAdded", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new { isStudent }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ChangePriorityAsync(Guid actorId, Guid complaintId, ChangeComplaintPriorityRequest request,
        CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        if (await userManager.IsInRoleAsync(actor, AppRoles.Student))
            throw new ForbiddenException("Students cannot change complaint priority.");
        await EnsureStaffDepartmentAccessAsync(actor, complaint, cancellationToken);
        var previous = complaint.Priority;
        complaint.Priority = request.Priority;
        var deadlines = ComplaintWorkflow.Deadlines(request.Priority);
        if (!complaint.FirstRespondedAt.HasValue) complaint.FirstResponseDueAt = complaint.CreatedAt.Add(deadlines.FirstResponse);
        if (!complaint.ResolvedAt.HasValue) complaint.ResolutionDueAt = complaint.CreatedAt.Add(deadlines.Resolution);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "ComplaintPriorityChanged", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new { previous, current = request.Priority, request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddAnonymousResponseAsync(AnonymousResponseRequest request, CancellationToken cancellationToken)
    {
        var access = await TrackAnonymousAsync(new AnonymousTrackRequest(request.ComplaintId, request.TrackingCode), cancellationToken);
        if (!access.Succeeded)
        {
            return Result.Failure(access.Error!.Code, access.Error.Message);
        }
        var complaintId = access.Value!.Id;
        dbContext.ComplaintResponses.Add(new ComplaintResponse
        {
            ComplaintId = complaintId, Body = request.Body.Trim(), Visibility = ResponseVisibility.Public,
            IsAnonymousComplainantResponse = true
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddInternalNoteAsync(Guid actorId, Guid complaintId, AddInternalNoteRequest request,
        CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        if (await userManager.IsInRoleAsync(actor, AppRoles.Student))
        {
            throw new ForbiddenException("Students cannot create internal notes.");
        }
        await EnsureStaffDepartmentAccessAsync(actor, complaint, cancellationToken);
        dbContext.InternalNotes.Add(new InternalNote
        {
            ComplaintId = complaint.Id, AuthorUserId = actorId, Body = request.Body.Trim()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyCollection<InternalNoteDto>> GetInternalNotesAsync(Guid actorId, Guid complaintId,
        CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        if (await userManager.IsInRoleAsync(actor, AppRoles.Student))
            throw new ForbiddenException("Students cannot access internal notes.");
        await EnsureStaffDepartmentAccessAsync(actor, complaint, cancellationToken);
        return await dbContext.InternalNotes.AsNoTracking().Where(x => x.ComplaintId == complaintId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new InternalNoteDto(x.Id, x.AuthorUserId, x.Body, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<Complaint> CreateComplaintAsync(Guid? studentId, string title, string description,
        ComplaintCategory category, Guid departmentId, bool anonymous, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var priority = category.DefaultPriority;
        var deadlines = ComplaintWorkflow.Deadlines(priority);
        var complaint = new Complaint
        {
            PublicId = await GeneratePublicIdAsync(cancellationToken), StudentUserId = studentId,
            Title = title.Trim(), Description = description.Trim(), CategoryId = category.Id,
            DepartmentId = departmentId, Priority = priority, Status = ComplaintStatus.Submitted,
            IsAnonymous = anonymous, FirstResponseDueAt = now.Add(deadlines.FirstResponse),
            ResolutionDueAt = now.Add(deadlines.Resolution)
        };
        complaint.StatusHistory.Add(new ComplaintStatusHistory
        {
            PreviousStatus = null, NewStatus = ComplaintStatus.Submitted,
            ChangedByUserId = studentId, Reason = "Complaint submitted."
        });
        dbContext.Complaints.Add(complaint);
        await dbContext.SaveChangesAsync(cancellationToken);
        return complaint;
    }

    private async Task<ComplaintCategory> GetActiveCategoryAsync(Guid categoryId, Guid departmentId,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == categoryId && x.IsActive,
            cancellationToken) ?? throw new NotFoundException("Complaint category not found.");
        if (category.DepartmentId.HasValue && category.DepartmentId != departmentId)
        {
            throw new ConflictException("The selected category does not belong to the selected department.");
        }
        if (!await dbContext.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive, cancellationToken))
        {
            throw new NotFoundException("Department not found.");
        }
        return category;
    }

    private async Task<IQueryable<Complaint>> AccessibleComplaintsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException("User not found.");
        if (await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            return dbContext.Complaints.AsNoTracking().Where(x => x.StudentUserId == userId && !x.IsAnonymous);
        }
        if (await permissionService.UserHasPermissionAsync(userId, Permissions.ComplaintsAllDepartments, cancellationToken))
        {
            return dbContext.Complaints.AsNoTracking();
        }
        if (await userManager.IsInRoleAsync(user, AppRoles.DepartmentOfficer) && user.DepartmentId.HasValue)
        {
            return dbContext.Complaints.AsNoTracking().Where(x => x.DepartmentId == user.DepartmentId.Value);
        }
        throw new ForbiddenException("You do not have access to complaints.");
    }

    private async Task EnsureStaffDepartmentAccessAsync(ApplicationUser user, Complaint complaint,
        CancellationToken cancellationToken)
    {
        if (await permissionService.UserHasPermissionAsync(user.Id, Permissions.ComplaintsAllDepartments, cancellationToken))
        {
            return;
        }
        if (!await userManager.IsInRoleAsync(user, AppRoles.DepartmentOfficer) || user.DepartmentId != complaint.DepartmentId)
        {
            throw new ForbiddenException("You cannot access complaints outside your department.");
        }
    }

    private async Task<string> GeneratePublicIdAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var id = $"UMAT-{clock.UtcNow:yyyy}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
            if (!await dbContext.Complaints.AnyAsync(x => x.PublicId == id, cancellationToken)) return id;
        }
        throw new InvalidOperationException("Unable to allocate a unique complaint ID.");
    }

    private static ComplaintDetailsDto ToDetails(Complaint complaint) => new(
        complaint.Id, complaint.PublicId, complaint.Title, complaint.Description,
        complaint.Category.Name, complaint.Department.Name, complaint.Priority, complaint.Status,
        complaint.IsAnonymous, complaint.CreatedAt, complaint.FirstResponseDueAt, complaint.ResolutionDueAt,
        complaint.StatusHistory.OrderBy(x => x.CreatedAt)
            .Select(x => new StatusHistoryDto(x.PreviousStatus, x.NewStatus, x.Reason, x.CreatedAt)).ToList(),
        complaint.Responses.Where(x => x.Visibility == ResponseVisibility.Public).OrderBy(x => x.CreatedAt)
            .Select(x => new ResponseDto(x.Id, x.Body,
                !x.IsAnonymousComplainantResponse && x.AuthorUserId != complaint.StudentUserId, x.CreatedAt)).ToList(),
        complaint.EvidenceFiles.OrderBy(x => x.CreatedAt)
            .Select(x => new EvidenceDto(x.Id, x.OriginalFileName, x.MimeType, x.SizeBytes, x.CreatedAt)).ToList());
}
