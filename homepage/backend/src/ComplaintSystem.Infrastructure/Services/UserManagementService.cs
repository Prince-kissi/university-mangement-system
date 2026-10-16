using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class UserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ApplicationDbContext dbContext,
    IAuditService auditService,
    ISessionService sessionService,
    IEmailSender emailSender) : IUserManagementService
{
    public async Task<StaffIdSuggestionDto> SuggestStaffIdAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var departmentCode = await dbContext.Departments.AsNoTracking()
            .Where(x => x.Id == departmentId && x.IsActive)
            .Select(x => x.Code)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(departmentCode))
            throw new KeyNotFoundException("The selected administrative unit was not found or is inactive.");

        var code = new string(departmentCode.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code)) code = "STAFF";
        var prefix = $"UMAT-{code}-";
        var assignedIds = await dbContext.Users.AsNoTracking()
            .Where(x => x.StaffId != null && x.StaffId.StartsWith(prefix))
            .Select(x => x.StaffId!)
            .ToListAsync(cancellationToken);
        var assigned = assignedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sequence = 1;
        while (assigned.Contains($"{prefix}{sequence:000}")) sequence++;
        return new StaffIdSuggestionDto($"{prefix}{sequence:000}");
    }

    public async Task<Result<UserSummaryDto>> CreateStaffAsync(Guid actorId, CreateStaffRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var actor = await GetUserAsync(actorId);
        var actorRoles = await userManager.GetRolesAsync(actor);
        var mayCreate = request.Role switch
        {
            AppRoles.DepartmentOfficer => actorRoles.Contains(AppRoles.SuperAdmin),
            AppRoles.Admin => actorRoles.Contains(AppRoles.SuperAdmin),
            _ => false
        };
        if (!mayCreate)
        {
            throw new ForbiddenException("You cannot create an account with that role.");
        }
        var email = request.Email.Trim().ToLowerInvariant();
        if (!UniversityEmailPolicy.IsApprovedStaffEmail(email) || await userManager.FindByEmailAsync(email) is not null)
        {
            return Result<UserSummaryDto>.Failure("account_creation_failed", "The staff account could not be created.");
        }
        if (request.Role == AppRoles.DepartmentOfficer &&
            (!request.DepartmentId.HasValue || !await dbContext.Departments.AnyAsync(x => x.Id == request.DepartmentId && x.IsActive, cancellationToken)))
        {
            return Result<UserSummaryDto>.Failure("invalid_administrative_unit", "Select an active administrative unit.");
        }
        var staffId = request.StaffId.Trim().Replace('–', '-').Replace('—', '-').ToUpperInvariant();
        if (request.Role == AppRoles.DepartmentOfficer && request.DepartmentId.HasValue)
            staffId = (await SuggestStaffIdAsync(request.DepartmentId.Value, cancellationToken)).StaffId;
        if (await dbContext.Users.AnyAsync(x => x.StaffId == staffId, cancellationToken))
        {
            return Result<UserSummaryDto>.Failure("staff_id_exists", "The staff ID is already assigned.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            StaffId = staffId,
            PhoneNumber = request.PhoneNumber.Trim(),
            Gender = request.Role == AppRoles.DepartmentOfficer ? request.Gender : null,
            DepartmentId = request.Role == AppRoles.DepartmentOfficer ? request.DepartmentId : null,
            AccountStatus = AccountStatus.Active,
            EmailConfirmed = true,
            ForcePasswordReset = true,
            ShowWelcomeMessage = true
        };
        var result = await userManager.CreateAsync(user, request.TemporaryPassword);
        if (!result.Succeeded)
        {
            await auditService.WriteAsync(actorId, "AccountCreation", "ApplicationUser", null, false, metadata,
                new { role = request.Role, errors = result.Errors.Select(x => x.Code) }, cancellationToken);
            return Result<UserSummaryDto>.Failure("account_creation_failed", string.Join(" ", result.Errors.Select(x => x.Description)));
        }
        await userManager.AddToRoleAsync(user, request.Role);
        var emailSent = true;
        string? warning = null;
        try
        {
            await emailSender.SendTemporaryPasswordAsync(
                email, user.DisplayName, request.TemporaryPassword, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            emailSent = false;
            warning = "The account was created, but the temporary-password email could not be sent because email delivery is not configured. Provide the temporary password to the officer through a secure channel.";
            await auditService.WriteAsync(actorId, "TemporaryPasswordDelivery", "ApplicationUser",
                user.Id.ToString(), false, metadata,
                new { role = request.Role, reason = exception.Message }, cancellationToken);
        }
        await auditService.WriteAsync(actorId, "AccountCreation", "ApplicationUser", user.Id.ToString(), true, metadata,
            new { role = request.Role, departmentId = user.DepartmentId, temporaryPasswordEmailSent = emailSent },
            cancellationToken);
        var summary = await ToSummaryAsync(user);
        return Result<UserSummaryDto>.Success(summary with
        {
            TemporaryPasswordEmailSent = emailSent,
            AccountCreationWarning = warning
        });
    }

    public async Task<PagedResult<UserSummaryDto>> GetUsersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = userManager.Users.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Email!.ToLower().Contains(term) ||
                x.FirstName.ToLower().Contains(term) || x.LastName.ToLower().Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var users = await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = new List<UserSummaryDto>(users.Count);
        foreach (var user in users)
        {
            items.Add(await ToSummaryAsync(user));
        }
        return new PagedResult<UserSummaryDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<UserSummaryDto>> GetAccountsAsync(AccountQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = dbContext.Users.AsNoTracking().AsQueryable();
        if (!request.IncludeArchived) query = query.Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.Email != null && x.Email.ToLower().Contains(term)) ||
                x.FirstName.ToLower().Contains(term) || x.LastName.ToLower().Contains(term) ||
                (x.StaffId != null && x.StaffId.ToLower().Contains(term)) ||
                (x.StudentReferenceNumber != null && x.StudentReferenceNumber.ToLower().Contains(term)));
        }
        if (request.DepartmentId.HasValue) query = query.Where(x => x.DepartmentId == request.DepartmentId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (request.Status.Equals("Archived", StringComparison.OrdinalIgnoreCase))
                query = query.Where(x => x.IsDeleted);
            else if (Enum.TryParse<AccountStatus>(request.Status, true, out var status))
                query = query.Where(x => !x.IsDeleted && x.AccountStatus == status);
        }
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var role = await roleManager.FindByNameAsync(request.Role);
            query = role is null
                ? query.Where(_ => false)
                : query.Where(user => dbContext.UserRoles.Any(userRole =>
                    userRole.UserId == user.Id && userRole.RoleId == role.Id));
        }

        query = (request.SortBy?.ToLowerInvariant(), request.SortDescending) switch
        {
            ("name", true) => query.OrderByDescending(x => x.LastName).ThenByDescending(x => x.FirstName),
            ("email", false) => query.OrderBy(x => x.Email),
            ("email", true) => query.OrderByDescending(x => x.Email),
            ("status", false) => query.OrderBy(x => x.AccountStatus),
            ("status", true) => query.OrderByDescending(x => x.AccountStatus),
            ("created", false) => query.OrderBy(x => x.CreatedAt),
            ("created", true) => query.OrderByDescending(x => x.CreatedAt),
            ("lastlogin", false) => query.OrderBy(x => x.LastLoginAt),
            ("lastlogin", true) => query.OrderByDescending(x => x.LastLoginAt),
            _ => query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
        };
        var total = await query.CountAsync(cancellationToken);
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = new List<UserSummaryDto>(users.Count);
        foreach (var user in users) items.Add(await ToSummaryAsync(user, cancellationToken));
        return new PagedResult<UserSummaryDto>(items, page, pageSize, total);
    }

    public async Task<AccountDetailsDto> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");
        var activity = await dbContext.AuditLogs.AsNoTracking()
            .Where(x => x.RelatedRecordType == "ApplicationUser" && x.RelatedRecordId == userId.ToString())
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new AccountActivityDto(x.Id, x.Action, x.Succeeded, x.CreatedAt, x.IpAddress, x.MetadataJson))
            .ToListAsync(cancellationToken);
        var loginHistory = await dbContext.AuditLogs.AsNoTracking()
            .Where(x => x.UserId == userId && x.Action.StartsWith("Login"))
            .OrderByDescending(x => x.CreatedAt).Take(50)
            .Select(x => new AccountActivityDto(x.Id, x.Action, x.Succeeded, x.CreatedAt, x.IpAddress, x.MetadataJson))
            .ToListAsync(cancellationToken);
        var submitted = await dbContext.Complaints.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StudentUserId == userId).OrderByDescending(x => x.CreatedAt).Take(50)
            .Select(x => new AccountComplaintDto(x.Id, x.PublicId, x.Title, x.Status.ToString(), x.CreatedAt))
            .ToListAsync(cancellationToken);
        var handledIds = dbContext.ComplaintAssignments.AsNoTracking()
            .Where(x => x.OfficerUserId == userId).Select(x => x.ComplaintId)
            .Union(dbContext.ComplaintResponses.AsNoTracking()
                .Where(x => x.AuthorUserId == userId).Select(x => x.ComplaintId));
        var handled = await dbContext.Complaints.IgnoreQueryFilters().AsNoTracking()
            .Where(x => handledIds.Contains(x.Id)).OrderByDescending(x => x.CreatedAt).Take(50)
            .Select(x => new AccountComplaintDto(x.Id, x.PublicId, x.Title, x.Status.ToString(), x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new AccountDetailsDto(await ToSummaryAsync(user, cancellationToken), activity, loginHistory, submitted, handled);
    }

    public async Task<Result> UpdateAccountAsync(Guid actorId, Guid userId, UpdateAccountRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (actorId == userId && request.Status != AccountStatus.Active)
            return Result.Failure("self_change_forbidden", "You cannot disable, suspend, lock, or archive your own account.");
        var actor = await GetUserAsync(actorId);
        var target = await GetUserAsync(userId);
        if (!await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin))
            throw new ForbiddenException("Only a Super Administrator can manage every account type.");
        var oldRoles = await userManager.GetRolesAsync(target);
        var oldRole = oldRoles.FirstOrDefault() ?? "";
        var targetIsSuper = oldRoles.Contains(AppRoles.SuperAdmin);
        if (targetIsSuper && actorId == userId && request.Role != AppRoles.SuperAdmin)
            return Result.Failure("self_role_change_forbidden", "You cannot change your own Super Administrator role.");
        if (targetIsSuper && (request.Status != AccountStatus.Active || request.Role != AppRoles.SuperAdmin))
            await EnsureAnotherActiveSuperAdminAsync(target.Id, cancellationToken);
        if (request.Role == AppRoles.DepartmentOfficer &&
            (!request.DepartmentId.HasValue || !await dbContext.Departments.AnyAsync(x => x.Id == request.DepartmentId && x.IsActive, cancellationToken)))
            return Result.Failure("invalid_administrative_unit", "Select an active administrative unit.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (request.Role == AppRoles.Student && !UniversityEmailPolicy.IsApprovedStudentEmail(email))
            return Result.Failure("invalid_student_email", "Use an approved UMaT student email address.");
        if (request.Role != AppRoles.Student && !UniversityEmailPolicy.IsApprovedStaffEmail(email))
            return Result.Failure("invalid_staff_email", "Staff email must end with @umat.edu.gh.");
        var emailOwner = await userManager.FindByEmailAsync(email);
        if (emailOwner is not null && emailOwner.Id != target.Id)
            return Result.Failure("email_exists", "The email address is already assigned.");
        var staffId = string.IsNullOrWhiteSpace(request.StaffId) ? null : request.StaffId.Trim().ToUpperInvariant();
        var reference = string.IsNullOrWhiteSpace(request.StudentReferenceNumber)
            ? null : UniversityEmailPolicy.NormalizeReferenceNumber(request.StudentReferenceNumber);
        if (staffId is not null && await dbContext.Users.AnyAsync(x => x.Id != target.Id && x.StaffId == staffId, cancellationToken))
            return Result.Failure("staff_id_exists", "The staff ID is already assigned.");
        if (reference is not null && await dbContext.Users.AnyAsync(x => x.Id != target.Id && x.StudentReferenceNumber == reference, cancellationToken))
            return Result.Failure("student_reference_exists", "The student reference number is already assigned.");
        if (reference is not null && await dbContext.Students.AnyAsync(x => x.UserId != target.Id && x.ReferenceNumber == reference, cancellationToken))
            return Result.Failure("student_reference_exists", "The student reference number is already assigned.");

        var oldValues = new
        {
            target.FirstName, target.LastName, target.Email, target.PhoneNumber, Role = oldRole,
            target.DepartmentId, target.AccountStatus, target.StaffId, target.StudentReferenceNumber
        };
        target.FirstName = request.FirstName.Trim();
        target.LastName = request.LastName.Trim();
        target.Email = email;
        target.UserName = email;
        target.NormalizedEmail = userManager.NormalizeEmail(email);
        target.NormalizedUserName = userManager.NormalizeName(email);
        target.PhoneNumber = request.PhoneNumber?.Trim();
        target.DepartmentId = request.Role == AppRoles.DepartmentOfficer ? request.DepartmentId : null;
        target.StaffId = request.Role == AppRoles.Student ? null : staffId;
        target.StudentReferenceNumber = request.Role == AppRoles.Student ? reference : null;
        target.AccountStatus = request.Status;
        if (request.Status == AccountStatus.Locked)
            target.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        else if (target.LockoutEnd > DateTimeOffset.UtcNow)
            target.LockoutEnd = null;
        var update = await userManager.UpdateAsync(target);
        if (!update.Succeeded)
            return Result.Failure("account_update_failed", string.Join(" ", update.Errors.Select(x => x.Description)));
        var student = await dbContext.Students.SingleOrDefaultAsync(x => x.UserId == target.Id, cancellationToken);
        if (request.Role == AppRoles.Student && student is not null)
        {
            student.FirstName = target.FirstName;
            student.LastName = target.LastName;
            student.Email = email;
            student.TelephoneNumber = target.PhoneNumber;
            if (reference is not null) student.ReferenceNumber = reference;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (!oldRoles.Contains(request.Role) || oldRoles.Count != 1)
        {
            await userManager.RemoveFromRolesAsync(target, oldRoles);
            var roleResult = await userManager.AddToRoleAsync(target, request.Role);
            if (!roleResult.Succeeded)
                return Result.Failure("role_update_failed", string.Join(" ", roleResult.Errors.Select(x => x.Description)));
        }
        if (request.Status != AccountStatus.Active || oldRole != request.Role)
            await sessionService.RevokeAllForUserAsync(target.Id, actorId, request.Reason, cancellationToken);
        var newValues = new
        {
            target.FirstName, target.LastName, target.Email, target.PhoneNumber, request.Role,
            target.DepartmentId, target.AccountStatus, target.StaffId, target.StudentReferenceNumber
        };
        await auditService.WriteAsync(actorId, oldRole == request.Role ? "AccountUpdated" : "UserRoleChanged",
            "ApplicationUser", target.Id.ToString(), true, metadata,
            new { performedByUserId = actorId, targetUserId = target.Id, targetUserRole = request.Role, oldValues, newValues, request.Reason },
            cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ArchiveAccountAsync(Guid actorId, Guid userId, string reason,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (actorId == userId)
            return Result.Failure("self_delete_forbidden", "You cannot delete your own account while logged in.");
        var actor = await GetUserAsync(actorId);
        var target = await GetUserAsync(userId);
        if (!await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin))
            throw new ForbiddenException("Only a Super Administrator can archive accounts.");
        var roles = await userManager.GetRolesAsync(target);
        if (roles.Contains(AppRoles.SuperAdmin))
            await EnsureAnotherActiveSuperAdminAsync(target.Id, cancellationToken);
        target.IsDeleted = true;
        target.DeletedAt = DateTimeOffset.UtcNow;
        target.AccountStatus = AccountStatus.Deactivated;
        target.SecurityStamp = Guid.NewGuid().ToString("N");
        await userManager.UpdateAsync(target);
        await sessionService.RevokeAllForUserAsync(target.Id, actorId, reason, cancellationToken);
        await auditService.WriteAsync(actorId, "AccountArchived", "ApplicationUser", target.Id.ToString(), true,
            metadata, new
            {
                performedByUserId = actorId, targetUserId = target.Id,
                targetUserRole = roles.FirstOrDefault(), oldValues = new { isDeleted = false },
                newValues = new { isDeleted = true, status = AccountStatus.Deactivated }, reason
            }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RestoreAccountAsync(Guid actorId, Guid userId, string reason,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var actor = await GetUserAsync(actorId);
        if (!await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin))
            throw new ForbiddenException("Only a Super Administrator can restore accounts.");
        var target = await dbContext.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");
        if (!target.IsDeleted) return Result.Failure("account_not_archived", "The account is not archived.");
        target.IsDeleted = false;
        target.DeletedAt = null;
        target.AccountStatus = AccountStatus.Active;
        target.LockoutEnd = null;
        await userManager.UpdateAsync(target);
        var roles = await userManager.GetRolesAsync(target);
        await auditService.WriteAsync(actorId, "AccountRestored", "ApplicationUser", target.Id.ToString(), true,
            metadata, new
            {
                performedByUserId = actorId, targetUserId = target.Id,
                targetUserRole = roles.FirstOrDefault(), oldValues = new { isDeleted = true },
                newValues = new { isDeleted = false, status = AccountStatus.Active }, reason
            }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetAccountStatusAsync(Guid actorId, Guid userId, UpdateAccountStatusRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (actorId == userId)
        {
            return Result.Failure("self_change_forbidden", "You cannot change your own account status.");
        }
        var actor = await GetUserAsync(actorId);
        var target = await GetUserAsync(userId);
        await EnsureCanManageAsync(actor, target, cancellationToken);
        if (await userManager.IsInRoleAsync(target, AppRoles.SuperAdmin) && request.Status != AccountStatus.Active)
        {
            await EnsureAnotherActiveSuperAdminAsync(target.Id, cancellationToken);
        }
        target.AccountStatus = request.Status;
        if (request.Status == AccountStatus.Locked)
        {
            await userManager.SetLockoutEndDateAsync(target, DateTimeOffset.UtcNow.AddYears(100));
        }
        await userManager.UpdateAsync(target);
        if (request.Status != AccountStatus.Active)
        {
            await sessionService.RevokeAllForUserAsync(target.Id, actorId, request.Reason, cancellationToken);
        }
        await auditService.WriteAsync(actorId, "AccountStatusChanged", "ApplicationUser", target.Id.ToString(), true,
            metadata, new { status = request.Status.ToString(), request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ChangeRoleAsync(Guid actorId, Guid userId, ChangeUserRoleRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (actorId == userId)
        {
            return Result.Failure("self_elevation_forbidden", "You cannot change your own role or permissions.");
        }
        if (request.Role == AppRoles.SuperAdmin || !AppRoles.All.Contains(request.Role, StringComparer.Ordinal))
        {
            return Result.Failure("role_change_forbidden", "That role cannot be assigned through this operation.");
        }
        var actor = await GetUserAsync(actorId);
        var target = await GetUserAsync(userId);
        await EnsureCanManageAsync(actor, target, cancellationToken);
        var actorIsSuper = await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin);
        if (request.Role == AppRoles.Admin && !actorIsSuper)
        {
            throw new ForbiddenException("Only a Super Admin can assign the Admin role.");
        }
        var oldRoles = await userManager.GetRolesAsync(target);
        if (oldRoles.Contains(AppRoles.SuperAdmin))
        {
            await EnsureAnotherActiveSuperAdminAsync(target.Id, cancellationToken);
        }
        await userManager.RemoveFromRolesAsync(target, oldRoles);
        await userManager.AddToRoleAsync(target, request.Role);
        target.DepartmentId = request.Role == AppRoles.DepartmentOfficer ? request.DepartmentId : null;
        await userManager.UpdateAsync(target);
        await sessionService.RevokeAllForUserAsync(target.Id, actorId, "Role changed", cancellationToken);
        await auditService.WriteAsync(actorId, "RoleChanged", "ApplicationUser", target.Id.ToString(), true,
            metadata, new { oldRoles, newRole = request.Role, request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UnlockAsync(Guid actorId, Guid userId, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var actor = await GetUserAsync(actorId);
        var target = await GetUserAsync(userId);
        await EnsureCanManageAsync(actor, target, cancellationToken);
        await userManager.SetLockoutEndDateAsync(target, null);
        await userManager.ResetAccessFailedCountAsync(target);
        if (target.AccountStatus == AccountStatus.Locked)
        {
            target.AccountStatus = AccountStatus.Active;
            await userManager.UpdateAsync(target);
        }
        await auditService.WriteAsync(actorId, "AccountUnlocked", "ApplicationUser", target.Id.ToString(), true,
            metadata, new { }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ForcePasswordResetAsync(Guid actorId, Guid userId, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var actor = await GetUserAsync(actorId);
        var target = await GetUserAsync(userId);
        await EnsureCanManageAsync(actor, target, cancellationToken);
        target.ForcePasswordReset = true;
        target.SecurityStamp = Guid.NewGuid().ToString("N");
        await userManager.UpdateAsync(target);
        await sessionService.RevokeAllForUserAsync(target.Id, actorId, "Password reset required", cancellationToken);
        await auditService.WriteAsync(actorId, "PasswordResetForced", "ApplicationUser", target.Id.ToString(), true,
            metadata, new { }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UpdateRolePermissionsAsync(Guid actorId, PermissionUpdateRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var actor = await GetUserAsync(actorId);
        if (!await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin))
        {
            throw new ForbiddenException("Only a Super Admin can manage permissions.");
        }
        if (await userManager.IsInRoleAsync(actor, request.Role))
        {
            return Result.Failure("self_permission_change_forbidden", "You cannot change permissions for your own active role.");
        }
        var role = await roleManager.FindByNameAsync(request.Role);
        if (role is null)
        {
            return Result.Failure("role_not_found", "Role not found.");
        }
        var allowed = Permissions.Defaults.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (request.Permissions.Any(x => !allowed.Contains(x)))
        {
            return Result.Failure("unknown_permission", "One or more permissions are invalid.");
        }
        var existing = await dbContext.RolePermissions.Where(x => x.RoleId == role.Id).ToListAsync(cancellationToken);
        dbContext.RolePermissions.RemoveRange(existing);
        dbContext.RolePermissions.AddRange(request.Permissions.Distinct(StringComparer.Ordinal).Select(permission => new RolePermission
        {
            RoleId = role.Id,
            Permission = permission,
            GrantedByUserId = actorId
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        var usersInRole = await userManager.GetUsersInRoleAsync(request.Role);
        foreach (var user in usersInRole)
        {
            await sessionService.RevokeAllForUserAsync(user.Id, actorId, "Role permissions changed", cancellationToken);
        }
        await auditService.WriteAsync(actorId, "PermissionsChanged", "IdentityRole", role.Id.ToString(), true,
            metadata, new { role = request.Role, permissions = request.Permissions, request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<RolePermissionsResponse> GetRolePermissionsAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await GetUserAsync(actorId);
        if (!await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin))
        {
            throw new ForbiddenException("Only a Super Admin can view role permissions.");
        }

        var available = Permissions.Defaults.Values.SelectMany(x => x)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var roles = await roleManager.Roles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var roleIds = roles.Select(x => x.Id).ToArray();
        var granted = await dbContext.RolePermissions.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId)).ToListAsync(cancellationToken);
        var result = roles.Where(x => x.Name is not null).Select(role => new RolePermissionDto(
            role.Name!,
            granted.Where(x => x.RoleId == role.Id).Select(x => x.Permission)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            role.Name is AppRoles.Admin or AppRoles.DepartmentOfficer)).ToArray();
        return new RolePermissionsResponse(available, result);
    }

    private async Task<ApplicationUser> GetUserAsync(Guid id) =>
        await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User not found.");

    private async Task EnsureCanManageAsync(ApplicationUser actor, ApplicationUser target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorIsSuper = await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin);
        var targetIsSuper = await userManager.IsInRoleAsync(target, AppRoles.SuperAdmin);
        var targetIsAdmin = await userManager.IsInRoleAsync(target, AppRoles.Admin);
        if (targetIsSuper || (targetIsAdmin && !actorIsSuper))
        {
            throw new ForbiddenException("You cannot manage this account.");
        }
    }

    private async Task EnsureAnotherActiveSuperAdminAsync(Guid excludedUserId, CancellationToken cancellationToken)
    {
        var superRole = await roleManager.FindByNameAsync(AppRoles.SuperAdmin) ?? throw new InvalidOperationException("SuperAdmin role is missing.");
        var activeCount = await (from userRole in dbContext.UserRoles
            join user in dbContext.Users on userRole.UserId equals user.Id
            where userRole.RoleId == superRole.Id && user.Id != excludedUserId && !user.IsDeleted && user.AccountStatus == AccountStatus.Active
            select user.Id).CountAsync(cancellationToken);
        if (activeCount == 0)
        {
            throw new ConflictException("The final active Super Admin cannot be suspended, deactivated or demoted.");
        }
    }

    private Task<UserSummaryDto> ToSummaryAsync(ApplicationUser user) => ToSummaryAsync(user, CancellationToken.None);

    private async Task<UserSummaryDto> ToSummaryAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var departmentName = user.DepartmentId.HasValue
            ? await dbContext.Departments.IgnoreQueryFilters().Where(x => x.Id == user.DepartmentId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
            : user.StudentDepartment;
        return new UserSummaryDto(user.Id, user.Email!, user.FirstName, user.LastName,
            user.AccountStatus.ToString(), roles.ToArray(), user.DepartmentId, user.LastLoginAt,
            user.StaffId, user.PhoneNumber, StudentReferenceNumber: user.StudentReferenceNumber,
            DepartmentName: departmentName, CreatedAt: user.CreatedAt, UpdatedAt: user.UpdatedAt,
            IsDeleted: user.IsDeleted, DeletedAt: user.DeletedAt);
    }
}
