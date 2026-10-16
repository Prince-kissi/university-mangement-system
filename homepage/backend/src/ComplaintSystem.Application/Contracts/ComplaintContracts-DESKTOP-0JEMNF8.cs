using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Application.Contracts;

public sealed record SubmitComplaintRequest(
    string Title, string Description, Guid CategoryId, Guid DepartmentId, PriorityLevel? Priority = null,
    bool IsAnonymous = false);
public sealed record SubmitAnonymousComplaintRequest(
    string Title, string Description, Guid CategoryId, Guid DepartmentId);
public sealed record ComplaintCreatedResponse(string ComplaintId, string? TrackingCode,
    string? AdministrativeUnit = null, DateTimeOffset? SubmittedAt = null,
    string Status = "Submitted", bool IsAnonymous = false);
public sealed record ComplaintSummaryDto(
    Guid Id, string ComplaintId, string Title, string Category, string Department,
    PriorityLevel Priority, ComplaintStatus Status, bool IsAnonymous,
    DateTimeOffset CreatedAt, DateTimeOffset? ResolutionDueAt);
public sealed record StudentDashboardDto(
    int TotalComplaints, int UnderReview, int Resolved, int ActionRequired,
    IReadOnlyCollection<ComplaintSummaryDto> RecentActivity);
public sealed record ComplaintDetailsDto(
    Guid Id, string ComplaintId, string Title, string Description, string Category, string Department,
    PriorityLevel Priority, ComplaintStatus Status, bool IsAnonymous, DateTimeOffset CreatedAt,
    DateTimeOffset? FirstResponseDueAt, DateTimeOffset? ResolutionDueAt,
    IReadOnlyCollection<StatusHistoryDto> StatusHistory,
    IReadOnlyCollection<ResponseDto> Responses,
    IReadOnlyCollection<EvidenceDto> Evidence);
public sealed record StatusHistoryDto(
    ComplaintStatus? PreviousStatus, ComplaintStatus NewStatus, string Reason, DateTimeOffset ChangedAt);
public sealed record ResponseDto(Guid Id, string Body, bool IsStaffResponse, DateTimeOffset CreatedAt,
    string? Initials = null, bool SignatureIncluded = false, string? SignedByName = null,
    string? AdministrativeUnit = null, DateTimeOffset? SignedAt = null);
public sealed record EvidenceDto(Guid Id, string FileName, string MimeType, long SizeBytes, DateTimeOffset CreatedAt);
public sealed record AssignComplaintRequest(Guid OfficerUserId, string Reason);
public sealed record ReassignComplaintRequest(Guid DepartmentId, Guid? OfficerUserId, string Reason);
public sealed record ChangeComplaintStatusRequest(ComplaintStatus Status, string Reason);
public sealed record AddResponseRequest(string Body, string AuthenticationMode = "None",
    string? OneTimeSignatureContentType = null, byte[]? OneTimeSignature = null);
public sealed record AddInternalNoteRequest(string Body);
public sealed record InternalNoteDto(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAt);
public sealed record ChangeComplaintPriorityRequest(PriorityLevel Priority, string Reason);
public sealed record AnonymousTrackRequest(string ComplaintId, string TrackingCode);
public sealed record AnonymousResponseRequest(string ComplaintId, string TrackingCode, string Body);
public sealed record EvidenceUpload(string FileName, string ContentType, long Length, Stream Content);
public sealed record EvidenceDownload(string FileName, string ContentType, Stream Content);
public sealed record ComplaintQuery(
    ComplaintStatus? Status = null, PriorityLevel? Priority = null, Guid? DepartmentId = null,
    Guid? CategoryId = null, string? Search = null, bool AssignedToMe = false, int Page = 1, int PageSize = 20);
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);
