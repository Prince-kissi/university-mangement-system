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
    ICurrentUser currentUser,
    INotificationService notificationService,
    IAuditService auditService,
    IFileStorage fileStorage,
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
        if (request.IsAnonymous && !category.AllowsAnonymous)
            return Result<ComplaintCreatedResponse>.Failure("category_not_anonymous", "This category does not accept anonymous complaints.");
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        var complaint = await CreateComplaintAsync(studentId, request.Title, request.Description, category,
            request.DepartmentId, request.IsAnonymous, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var unitName = await dbContext.Departments.Where(x => x.Id == request.DepartmentId)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        return Result<ComplaintCreatedResponse>.Success(new ComplaintCreatedResponse(complaint.PublicId, null,
            unitName, complaint.CreatedAt, complaint.Status.ToString(), complaint.IsAnonymous));
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
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        var complaint = await CreateComplaintAsync(null, request.Title, request.Description, category,
            request.DepartmentId, true, cancellationToken);
        var rawCode = trackingService.GenerateCode();
        dbContext.AnonymousTrackingRecords.Add(new AnonymousTrackingRecord
        {
            ComplaintId = complaint.Id,
            TrackingCodeHash = trackingService.HashCode(rawCode)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var unitName = await dbContext.Departments.Where(x => x.Id == request.DepartmentId)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        return Result<ComplaintCreatedResponse>.Success(new ComplaintCreatedResponse(complaint.PublicId, rawCode,
            unitName, complaint.CreatedAt, complaint.Status.ToString(), true));
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

    public async Task<StudentDashboardDto> GetStudentDashboardAsync(Guid studentId,
        CancellationToken cancellationToken)
    {
        var complaints = await AccessibleComplaintsAsync(studentId, cancellationToken);
        var total = await complaints.CountAsync(cancellationToken);
        var underReviewStatuses = new[]
        {
            ComplaintStatus.Submitted, ComplaintStatus.Unassigned, ComplaintStatus.Assigned, ComplaintStatus.Acknowledged,
            ComplaintStatus.UnderReview, ComplaintStatus.InvestigationInProgress, ComplaintStatus.Escalated,
            ComplaintStatus.Reopened
        };
        var underReview = await complaints.CountAsync(x => underReviewStatuses.Contains(x.Status), cancellationToken);
        var resolved = await complaints.CountAsync(
            x => x.Status == ComplaintStatus.Resolved || x.Status == ComplaintStatus.Closed,
            cancellationToken);
        var actionRequired = await complaints.CountAsync(
            x => x.Status == ComplaintStatus.AwaitingStudentResponse,
            cancellationToken);
        var recent = await complaints.OrderByDescending(x => x.UpdatedAt)
            .Take(5)
            .Select(x => new ComplaintSummaryDto(x.Id, x.PublicId, x.Title, x.Category.Name, x.Department.Name,
                x.Priority, x.Status, x.IsAnonymous, x.CreatedAt, x.ResolutionDueAt))
            .ToListAsync(cancellationToken);

        return new StudentDashboardDto(total, underReview, resolved, actionRequired, recent);
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

    public async Task<Result> ClaimAsync(Guid actorId, Guid complaintId, CancellationToken cancellationToken)
    {
        var officer = await userManager.FindByIdAsync(actorId.ToString())
            ?? throw new ForbiddenException("Officer not found.");
        var complaint = await dbContext.Complaints.Include(x => x.Assignments).Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        if (!await userManager.IsInRoleAsync(officer, AppRoles.DepartmentOfficer) ||
            officer.AccountStatus != AccountStatus.Active || officer.DepartmentId != complaint.DepartmentId)
            throw new ForbiddenException("You cannot claim complaints outside your administrative unit.");
        if (complaint.Assignments.Any(x => x.IsCurrent))
            return Result.Failure("already_claimed", "This complaint has already been claimed by another officer.");

        complaint.Assignments.Add(new ComplaintAssignment
        {
            ComplaintId = complaint.Id,
            OfficerUserId = officer.Id,
            AssignedByUserId = officer.Id,
            AssignedAt = clock.UtcNow,
            Reason = "Claimed from the administrative unit shared queue.",
            IsCurrent = true
        });
        var previous = complaint.Status;
        complaint.Status = ComplaintStatus.Assigned;
        complaint.StatusHistory.Add(new ComplaintStatusHistory
        {
            ComplaintId = complaint.Id, PreviousStatus = previous, NewStatus = ComplaintStatus.Assigned,
            ChangedByUserId = officer.Id, Reason = "Complaint claimed by a Department Officer."
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure("already_claimed", "This complaint was claimed by another officer.");
        }
        await auditService.WriteAsync(officer.Id, "ComplaintClaimed", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new { officerId = officer.Id, administrativeUnitId = complaint.DepartmentId }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReassignAsync(Guid actorId, Guid complaintId, ReassignComplaintRequest request,
        CancellationToken cancellationToken)
    {
        var targetUnit = await dbContext.Departments.SingleOrDefaultAsync(x => x.Id == request.DepartmentId && x.IsActive,
            cancellationToken) ?? throw new NotFoundException("Administrative unit not found.");
        var complaint = await dbContext.Complaints.Include(x => x.Assignments).Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        ApplicationUser? selectedOfficer = null;
        if (request.OfficerUserId.HasValue)
        {
            selectedOfficer = await userManager.FindByIdAsync(request.OfficerUserId.Value.ToString())
                ?? throw new NotFoundException("Officer not found.");
            if (!await userManager.IsInRoleAsync(selectedOfficer, AppRoles.DepartmentOfficer) ||
                selectedOfficer.AccountStatus != AccountStatus.Active || selectedOfficer.DepartmentId != targetUnit.Id)
                return Result.Failure("invalid_assignment", "The selected officer is not active in the target administrative unit.");
        }
        var previousUnitId = complaint.DepartmentId;
        foreach (var assignment in complaint.Assignments.Where(x => x.IsCurrent))
        {
            assignment.IsCurrent = false;
            assignment.UnassignedAt = clock.UtcNow;
        }
        complaint.DepartmentId = targetUnit.Id;
        if (selectedOfficer is not null)
        {
            complaint.Assignments.Add(new ComplaintAssignment
            {
                ComplaintId = complaint.Id, OfficerUserId = selectedOfficer.Id, AssignedByUserId = actorId,
                AssignedAt = clock.UtcNow, Reason = request.Reason.Trim(), IsCurrent = true
            });
            complaint.Status = ComplaintStatus.Assigned;
        }
        else
        {
            var hasOfficers = await ActiveOfficerIdsAsync(targetUnit.Id, cancellationToken);
            complaint.Status = hasOfficers.Count > 0 ? ComplaintStatus.Submitted : ComplaintStatus.Unassigned;
            var recipients = hasOfficers.Count > 0 ? hasOfficers : await AdministratorIdsAsync(cancellationToken);
            dbContext.Notifications.AddRange(recipients.Select(userId => new Notification
            {
                UserId = userId, Title = hasOfficers.Count > 0 ? "New Complaint Assigned" : "Complaint Requires Assignment",
                Message = hasOfficers.Count > 0
                    ? $"A complaint has been reassigned to your administrative unit. Reference: {complaint.PublicId}."
                    : $"Complaint {complaint.PublicId} requires assignment because the selected administrative unit has no active officer.",
                Type = NotificationType.ComplaintUpdate,
                Link = $"/dashboards/admin/complaint-details.html?id={complaint.Id}"
            }));
        }
        complaint.StatusHistory.Add(new ComplaintStatusHistory
        {
            ComplaintId = complaint.Id, NewStatus = complaint.Status, ChangedByUserId = actorId,
            Reason = $"Administrative unit changed. {request.Reason.Trim()}"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (selectedOfficer is not null)
            await notificationService.SendToUserAsync(selectedOfficer.Id, "New Complaint Assigned",
                $"Complaint {complaint.PublicId} has been assigned to you.", NotificationType.ComplaintUpdate,
                $"/dashboards/department-officer/complaint-details.html?id={complaint.Id}", cancellationToken);
        await auditService.WriteAsync(actorId, "ComplaintReassigned", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new { previousUnitId, administrativeUnitId = targetUnit.Id,
                assignedOfficerId = selectedOfficer?.Id, request.Reason }, cancellationToken);
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
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        var isStudent = await userManager.IsInRoleAsync(actor, AppRoles.Student);
        if (isStudent && complaint.StudentUserId != actorId)
        {
            throw new ForbiddenException("You cannot respond to another student's complaint.");
        }
        string? responseSignaturePath = null;
        string? responseInitials = null;
        DateTimeOffset? signedAt = null;
        string? officerName = null;
        string? unitName = null;
        var includeSignature = false;
        if (!isStudent)
        {
            await EnsureStaffDepartmentAccessAsync(actor, complaint, cancellationToken);
            complaint.FirstRespondedAt ??= clock.UtcNow;
            var isOfficer = await userManager.IsInRoleAsync(actor, AppRoles.DepartmentOfficer);
            var mode = request.AuthenticationMode.Trim();
            if (mode != "None" && !isOfficer)
                throw new ForbiddenException("Only Department Officers can authenticate responses with signatures or initials.");
            if (isOfficer)
            {
                officerName = actor.DisplayName;
                unitName = await dbContext.Departments.Where(x => x.Id == actor.DepartmentId)
                    .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken);
                if (mode is "Initials" or "Both")
                {
                    if (string.IsNullOrWhiteSpace(actor.Initials))
                        return Result.Failure("initials_not_configured", "Save your initials before using them.");
                    responseInitials = actor.Initials;
                }
                if (mode is "Signature" or "Both")
                {
                    if (actor.SignatureFilePath is null)
                        return Result.Failure("signature_not_configured", "No saved signature is available.");
                    await using var source = await fileStorage.OpenReadAsync(actor.SignatureFilePath, cancellationToken);
                    responseSignaturePath = await fileStorage.SaveAsync(
                        $"response-signature-{Guid.NewGuid():N}{Path.GetExtension(actor.SignatureFilePath)}", source, cancellationToken);
                    includeSignature = true;
                }
                if (mode == "OneTimeSignature")
                {
                    if (request.OneTimeSignature is null || request.OneTimeSignature.Length is <= 0 or > 2 * 1024 * 1024)
                        return Result.Failure("invalid_signature", "Select a signature image no larger than 2 MB.");
                    var extension = DetectSignatureExtension(request.OneTimeSignature);
                    if (extension is null)
                        return Result.Failure("invalid_signature", "Only genuine PNG, JPG, or JPEG images are accepted.");
                    await using var source = new MemoryStream(request.OneTimeSignature);
                    responseSignaturePath = await fileStorage.SaveAsync($"response-signature-{Guid.NewGuid():N}{extension}", source, cancellationToken);
                    includeSignature = true;
                }
                if (includeSignature || responseInitials is not null) signedAt = clock.UtcNow;
            }
        }
        var response = new ComplaintResponse
        {
            ComplaintId = complaint.Id, AuthorUserId = actorId, Body = request.Body.Trim(),
            Visibility = ResponseVisibility.Public, ResponseSignatureFilePath = responseSignaturePath,
            ResponseInitials = responseInitials, SignatureIncluded = includeSignature, SignedAt = signedAt,
            SignedByOfficerId = signedAt.HasValue ? actorId : null, SignedByOfficerName = officerName,
            SignedByAdministrativeUnit = unitName
        };
        dbContext.ComplaintResponses.Add(response);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (!isStudent && complaint.StudentUserId.HasValue)
        {
            await notificationService.SendToUserAsync(complaint.StudentUserId.Value, "New response to your complaint",
                $"A staff member responded to complaint {complaint.PublicId}.", NotificationType.ComplaintUpdate,
                $"/track-complaint.html?id={complaint.Id}", cancellationToken);
        }
        await auditService.WriteAsync(actorId, "ComplaintResponseAdded", "Complaint", complaint.Id.ToString(), true,
            metadata, new { isStudent, officerId = actorId, administrativeUnitId = actor.DepartmentId }, cancellationToken);
        if (includeSignature)
            await auditService.WriteAsync(actorId, "SignatureUsedOnComplaintResponse", "ComplaintResponse", response.Id.ToString(), true,
                metadata, new { officerId = actorId, administrativeUnitId = actor.DepartmentId, complaintId }, cancellationToken);
        if (responseInitials is not null)
            await auditService.WriteAsync(actorId, "InitialsUsedOnComplaintResponse", "ComplaintResponse", response.Id.ToString(), true,
                metadata, new { officerId = actorId, administrativeUnitId = actor.DepartmentId, complaintId }, cancellationToken);
        return Result.Success();
    }

    private static string? DetectSignatureExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        return null;
    }

    public async Task<EvidenceDownload> GetResponseSignatureAsync(Guid userId, Guid complaintId, Guid responseId,
        CancellationToken cancellationToken)
    {
        _ = await GetByIdAsync(userId, complaintId, cancellationToken);
        var response = await dbContext.ComplaintResponses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == responseId && x.ComplaintId == complaintId, cancellationToken)
            ?? throw new NotFoundException("Response not found.");
        if (!response.SignatureIncluded || response.ResponseSignatureFilePath is null)
            throw new NotFoundException("No signature is attached to this response.");
        var extension = Path.GetExtension(response.ResponseSignatureFilePath);
        return new EvidenceDownload($"response-signature{extension}",
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
            await fileStorage.OpenReadAsync(response.ResponseSignatureFilePath, cancellationToken));
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
        var officerIds = await (from user in dbContext.Users.AsNoTracking()
                                join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                                join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                where user.DepartmentId == departmentId && !user.IsDeleted &&
                                      user.AccountStatus == AccountStatus.Active && role.Name == AppRoles.DepartmentOfficer
                                select user.Id)
            .Distinct().ToListAsync(cancellationToken);
        var initialStatus = officerIds.Count > 0 ? ComplaintStatus.Submitted : ComplaintStatus.Unassigned;
        var complaint = new Complaint
        {
            PublicId = await GeneratePublicIdAsync(cancellationToken), StudentUserId = studentId,
            Title = title.Trim(), Description = description.Trim(), CategoryId = category.Id,
            DepartmentId = departmentId, Priority = priority, Status = initialStatus,
            IsAnonymous = anonymous, FirstResponseDueAt = now.Add(deadlines.FirstResponse),
            ResolutionDueAt = now.Add(deadlines.Resolution)
        };
        complaint.StatusHistory.Add(new ComplaintStatusHistory
        {
            PreviousStatus = null, NewStatus = initialStatus,
            ChangedByUserId = studentId, Reason = officerIds.Count > 0
                ? "Complaint submitted and routed automatically to the administrative unit shared queue."
                : "Complaint submitted without an active officer and routed to administrators for assignment."
        });
        dbContext.Complaints.Add(complaint);
        if (officerIds.Count > 0)
        {
            dbContext.Notifications.AddRange(officerIds.Select(officerId => new Notification
            {
                UserId = officerId,
                Title = "New Complaint Assigned",
                Message = $"A new complaint has been submitted to your administrative unit. Reference: {complaint.PublicId}.",
                Type = NotificationType.ComplaintUpdate,
                Link = $"/dashboards/department-officer/complaint-details.html?id={complaint.Id}"
            }));
        }
        else
        {
            var administratorIds = await (from user in dbContext.Users.AsNoTracking()
                                          join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                                          join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                          where !user.IsDeleted && user.AccountStatus == AccountStatus.Active &&
                                                (role.Name == AppRoles.Admin || role.Name == AppRoles.SuperAdmin)
                                          select user.Id).Distinct().ToListAsync(cancellationToken);
            dbContext.Notifications.AddRange(administratorIds.Select(userId => new Notification
            {
                UserId = userId,
                Title = "Complaint Requires Assignment",
                Message = $"Complaint {complaint.PublicId} requires assignment because no active officer is assigned to the selected administrative unit.",
                Type = NotificationType.ComplaintUpdate,
                Link = $"/dashboards/admin/complaint-details.html?id={complaint.Id}"
            }));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(studentId, "ComplaintAutomaticallyRouted", "Complaint", complaint.Id.ToString(), true,
            new RequestMetadata("system", null), new
            {
                complaintId = complaint.PublicId,
                administrativeUnitId = departmentId,
                assignmentType = "AUTOMATIC",
                routingStrategy = "SHARED_QUEUE",
                eligibleOfficerIds = officerIds,
                unassigned = officerIds.Count == 0,
                anonymous
            }, cancellationToken);
        return complaint;
    }

    private async Task<List<Guid>> ActiveOfficerIdsAsync(Guid departmentId, CancellationToken cancellationToken) =>
        await (from user in dbContext.Users.AsNoTracking()
               join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
               join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
               where user.DepartmentId == departmentId && !user.IsDeleted &&
                     user.AccountStatus == AccountStatus.Active && role.Name == AppRoles.DepartmentOfficer
               select user.Id).Distinct().ToListAsync(cancellationToken);

    private async Task<List<Guid>> AdministratorIdsAsync(CancellationToken cancellationToken) =>
        await (from user in dbContext.Users.AsNoTracking()
               join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
               join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
               where !user.IsDeleted && user.AccountStatus == AccountStatus.Active &&
                     (role.Name == AppRoles.Admin || role.Name == AppRoles.SuperAdmin)
               select user.Id).Distinct().ToListAsync(cancellationToken);

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
        if (currentUser.ActiveRole == AppRoles.DepartmentOfficer)
        {
            if (!currentUser.DepartmentId.HasValue) return dbContext.Complaints.AsNoTracking().Where(_ => false);
            return dbContext.Complaints.AsNoTracking().Where(x => x.DepartmentId == currentUser.DepartmentId.Value);
        }
        if (await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            return dbContext.Complaints.AsNoTracking().Where(x => x.StudentUserId == userId);
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
        if (currentUser.ActiveRole == AppRoles.DepartmentOfficer)
        {
            if (!currentUser.DepartmentId.HasValue || currentUser.DepartmentId.Value != complaint.DepartmentId)
                throw new ForbiddenException("You cannot access complaints outside your active department.");
            return;
        }
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
            var id = $"UMAT-CMP-{clock.UtcNow:yyyy}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
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
                !x.IsAnonymousComplainantResponse && x.AuthorUserId != complaint.StudentUserId, x.CreatedAt,
                x.ResponseInitials, x.SignatureIncluded, x.SignedByOfficerName,
                x.SignedByAdministrativeUnit, x.SignedAt)).ToList(),
        complaint.EvidenceFiles.OrderBy(x => x.CreatedAt)
            .Select(x => new EvidenceDto(x.Id, x.OriginalFileName, x.MimeType, x.SizeBytes, x.CreatedAt)).ToList());
}
