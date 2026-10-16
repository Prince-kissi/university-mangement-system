using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Application.Abstractions;

public sealed record RequestMetadata(string IpAddress, string? UserAgent);

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? DepartmentId { get; }
    string? SessionId { get; }
    string? ActualRole { get; }
    string? ActiveRole { get; }
    bool IsInRole(string role);
}

public interface IAuthenticationService
{
    Task<Result<StudentRegistrationPendingResponse>> RegisterStudentAsync(RegisterStudentRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<StudentVerificationSendResponse>> SendStudentVerificationCodeAsync(StudentVerificationSendRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> VerifyStudentRegistrationCodeAsync(StudentVerificationCodeRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> LoginStudentAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    InstitutionalLoginConfigurationDto GetInstitutionalLoginConfiguration();
    Task<Result<AuthResponse>> LoginInstitutionallyAsync(InstitutionalLoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> LoginStaffAsync(StaffLoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> VerifyTwoFactorAsync(TwoFactorRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<TwoFactorSetupResponse>> BeginTwoFactorSetupAsync(TwoFactorSetupRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> EnableTwoFactorAsync(TwoFactorEnableRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task RequestEmailVerificationAsync(string email, CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(PasswordResetRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task RequestAdminPasswordResetAsync(PasswordResetRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<StudentRecoveryLookupResponse>> BeginStudentRecoveryAsync(StudentRecoveryLookupRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> SendStudentRecoveryCodeAsync(StudentRecoverySendRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<StudentRecoveryVerifyResponse>> VerifyStudentRecoveryCodeAsync(StudentRecoveryVerifyRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> CompleteStudentRecoveryAsync(StudentRecoveryCompleteRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result<CurrentUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);
    Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
    Task LogoutAsync(Guid? userId, string? sessionId, RequestMetadata metadata, CancellationToken cancellationToken);
}

public interface IUserManagementService
{
    Task<StaffIdSuggestionDto> SuggestStaffIdAsync(Guid departmentId, CancellationToken cancellationToken);
    Task<Result<UserSummaryDto>> CreateStaffAsync(Guid actorId, CreateStaffRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<PagedResult<UserSummaryDto>> GetUsersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<Result> SetAccountStatusAsync(Guid actorId, Guid userId, UpdateAccountStatusRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> ChangeRoleAsync(Guid actorId, Guid userId, ChangeUserRoleRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> UnlockAsync(Guid actorId, Guid userId, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> ForcePasswordResetAsync(Guid actorId, Guid userId, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> UpdateRolePermissionsAsync(Guid actorId, PermissionUpdateRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<RolePermissionsResponse> GetRolePermissionsAsync(Guid actorId, CancellationToken cancellationToken);
    Task<PagedResult<UserSummaryDto>> GetAccountsAsync(AccountQuery query, CancellationToken cancellationToken);
    Task<AccountDetailsDto> GetAccountAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> UpdateAccountAsync(Guid actorId, Guid userId, UpdateAccountRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> ArchiveAccountAsync(Guid actorId, Guid userId, string reason, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> RestoreAccountAsync(Guid actorId, Guid userId, string reason, RequestMetadata metadata, CancellationToken cancellationToken);
}

public interface IComplaintService
{
    Task<Result<ComplaintCreatedResponse>> SubmitAsync(Guid studentId, SubmitComplaintRequest request, CancellationToken cancellationToken);
    Task<Result<ComplaintCreatedResponse>> SubmitAnonymousAsync(SubmitAnonymousComplaintRequest request, CancellationToken cancellationToken);
    Task<Result<ComplaintDetailsDto>> TrackAnonymousAsync(AnonymousTrackRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ComplaintSummaryDto>> GetAsync(Guid userId, ComplaintQuery query, CancellationToken cancellationToken);
    Task<StudentDashboardDto> GetStudentDashboardAsync(Guid studentId, CancellationToken cancellationToken);
    Task<ComplaintDetailsDto> GetByIdAsync(Guid userId, Guid complaintId, CancellationToken cancellationToken);
    Task<Result> AssignAsync(Guid actorId, Guid complaintId, AssignComplaintRequest request, CancellationToken cancellationToken);
    Task<Result> ClaimAsync(Guid actorId, Guid complaintId, CancellationToken cancellationToken);
    Task<Result> ReassignAsync(Guid actorId, Guid complaintId, ReassignComplaintRequest request, CancellationToken cancellationToken);
    Task<Result> ChangeStatusAsync(Guid actorId, Guid complaintId, ChangeComplaintStatusRequest request, CancellationToken cancellationToken);
    Task<Result> ChangePriorityAsync(Guid actorId, Guid complaintId, ChangeComplaintPriorityRequest request, CancellationToken cancellationToken);
    Task<Result> AddResponseAsync(Guid actorId, Guid complaintId, AddResponseRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<EvidenceDownload> GetResponseSignatureAsync(Guid userId, Guid complaintId, Guid responseId, CancellationToken cancellationToken);
    Task<Result> AddAnonymousResponseAsync(AnonymousResponseRequest request, CancellationToken cancellationToken);
    Task<Result> AddInternalNoteAsync(Guid actorId, Guid complaintId, AddInternalNoteRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<InternalNoteDto>> GetInternalNotesAsync(Guid actorId, Guid complaintId, CancellationToken cancellationToken);
}

public interface IEvidenceService
{
    Task<Result<EvidenceDto>> UploadAsync(Guid? userId, Guid complaintId, EvidenceUpload upload, string? anonymousTrackingCode, CancellationToken cancellationToken);
    Task<EvidenceDownload> DownloadAsync(Guid userId, Guid evidenceId, EvidenceAccessType accessType, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> SoftDeleteAsync(Guid actorId, Guid evidenceId, CancellationToken cancellationToken);
}

public interface IAdministrationService
{
    Task<IReadOnlyCollection<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken);
    Task<Result<DepartmentDto>> UpsertDepartmentAsync(Guid actorId, Guid? id, DepartmentRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteDepartmentAsync(Guid actorId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<Result<CategoryDto>> UpsertCategoryAsync(Guid actorId, Guid? id, CategoryRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteCategoryAsync(Guid actorId, Guid id, CancellationToken cancellationToken);
}

public interface IAdminDashboardService
{
    Task<AdminDashboardDto> GetAsync(Guid administratorId, AdminDashboardQuery query,
        CancellationToken cancellationToken);
}

public interface ISetupService
{
    Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken);
    Task<Result<FirstSuperAdminCreatedDto>> CreateFirstSuperAdminAsync(
        CreateFirstSuperAdminRequest request, CancellationToken cancellationToken);
}

public interface INotificationService
{
    Task<IReadOnlyCollection<NotificationDto>> GetForUserAsync(Guid userId, bool unreadOnly, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);
    Task<AnnouncementResultDto> SendAnnouncementAsync(Guid actorId, AnnouncementRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<AnnouncementResultDto> PreviewAnnouncementAsync(Guid actorId, AnnouncementRequest request, CancellationToken cancellationToken);
    Task SendToUserAsync(Guid userId, string title, string message, NotificationType type, string? link, CancellationToken cancellationToken);
}

public interface IReportingService
{
    Task<ReportSummaryDto> GetSummaryAsync(ReportFilter filter, CancellationToken cancellationToken);
    Task<ExportFile> ExportAsync(string format, ReportFilter filter, CancellationToken cancellationToken);
}

public interface ISystemManagementService
{
    Task<IReadOnlyCollection<AuditLogDto>> GetAuditLogsAsync(AuditQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SettingDto>> GetSettingsAsync(CancellationToken cancellationToken);
    Task<Result> UpdateSettingAsync(Guid actorId, string key, UpdateSettingRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken);
    Task<Result> RevokeSessionAsync(Guid actorId, Guid sessionId, string reason, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupDto>> GetBackupsAsync(CancellationToken cancellationToken);
    Task<Result<BackupDto>> InitiateBackupAsync(Guid actorId, RequestMetadata metadata, CancellationToken cancellationToken);
    Task<Result> RestoreBackupAsync(Guid actorId, Guid backupId, RestoreBackupRequest request, RequestMetadata metadata, CancellationToken cancellationToken);
}
