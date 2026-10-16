using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Application.Contracts;

public sealed record CreateStaffRequest(
    string Email, string FirstName, string LastName, string StaffId, string PhoneNumber,
    string Role, Guid? DepartmentId, string TemporaryPassword, string? Gender = null);
public sealed record StaffIdSuggestionDto(string StaffId);
public sealed record UpdateAccountStatusRequest(AccountStatus Status, string Reason);
public sealed record ChangeUserRoleRequest(string Role, Guid? DepartmentId, string Reason);
public sealed record UserSummaryDto(
    Guid Id, string Email, string FirstName, string LastName, string AccountStatus,
    IReadOnlyCollection<string> Roles, Guid? DepartmentId, DateTimeOffset? LastLoginAt,
    string? StaffId = null, string? PhoneNumber = null,
    bool? TemporaryPasswordEmailSent = null, string? AccountCreationWarning = null,
    string? StudentReferenceNumber = null, string? DepartmentName = null,
    DateTimeOffset? CreatedAt = null, DateTimeOffset? UpdatedAt = null,
    bool IsDeleted = false, DateTimeOffset? DeletedAt = null);
public sealed record AccountQuery(
    int Page = 1, int PageSize = 20, string? Search = null, string? Role = null,
    Guid? DepartmentId = null, string? Status = null, string? SortBy = null,
    bool SortDescending = false, bool IncludeArchived = true);
public sealed record UpdateAccountRequest(
    string FirstName, string LastName, string Email, string? PhoneNumber, string Role,
    Guid? DepartmentId, string? StaffId, string? StudentReferenceNumber, AccountStatus Status,
    string Reason);
public sealed record AccountComplaintDto(Guid Id, string PublicId, string Title, string Status, DateTimeOffset CreatedAt);
public sealed record AccountActivityDto(
    Guid Id, string Action, bool Succeeded, DateTimeOffset Timestamp, string IpAddress, string MetadataJson);
public sealed record AccountDetailsDto(
    UserSummaryDto Account, IReadOnlyCollection<AccountActivityDto> Activity,
    IReadOnlyCollection<AccountActivityDto> LoginHistory,
    IReadOnlyCollection<AccountComplaintDto> ComplaintsSubmitted,
    IReadOnlyCollection<AccountComplaintDto> ComplaintsHandled);
public sealed record DepartmentRequest(string Code, string Name, string? ContactEmail, bool IsActive = true);
public sealed record DepartmentDto(Guid Id, string Code, string Name, string? ContactEmail, bool IsActive);
public sealed record CategoryRequest(
    string Name, string? Description, Guid? DepartmentId, PriorityLevel DefaultPriority,
    bool AllowsAnonymous, bool IsActive = true);
public sealed record CategoryDto(
    Guid Id, string Name, string? Description, Guid? DepartmentId,
    PriorityLevel DefaultPriority, bool AllowsAnonymous, bool IsActive);
public sealed record PermissionUpdateRequest(string Role, IReadOnlyCollection<string> Permissions, string Reason);
public sealed record RolePermissionDto(string Role, IReadOnlyCollection<string> Permissions, bool Editable);
public sealed record RolePermissionsResponse(
    IReadOnlyCollection<string> AvailablePermissions,
    IReadOnlyCollection<RolePermissionDto> Roles);
public sealed record SessionDto(
    Guid Id, Guid UserId, string IpAddress, string? UserAgent, DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
