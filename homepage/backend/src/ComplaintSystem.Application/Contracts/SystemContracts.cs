using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Application.Contracts;

public sealed record NotificationDto(
    Guid Id, string Title, string Message, NotificationType Type, string? Link,
    bool IsRead, DateTimeOffset CreatedAt);
public sealed record AnnouncementRequest(
    string Title, string Message, string Category = "General Notice", string Priority = "Normal",
    bool Everyone = false, IReadOnlyCollection<string>? RecipientRoles = null, Guid? DepartmentId = null,
    string? StudentLevel = null, IReadOnlyCollection<Guid>? UserIds = null,
    IReadOnlyCollection<string>? DeliveryChannels = null, DateTimeOffset? PublishAt = null,
    DateTimeOffset? ExpiresAt = null, bool RequireAcknowledgement = false, bool SaveAsDraft = false);
public sealed record AnnouncementResultDto(int EstimatedRecipients, int Delivered, int Failed, string Status);
public sealed record AuditLogDto(
    Guid Id, Guid? UserId, string Action, DateTimeOffset Timestamp, string RelatedRecordType,
    string? RelatedRecordId, string IpAddress, string? UserAgent, bool Succeeded, string MetadataJson);
public sealed record AuditQuery(string? Action = null, Guid? UserId = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, int Page = 1, int PageSize = 50);
public sealed record SettingDto(string Key, string Value, string? Description, bool IsSensitive);
public sealed record UpdateSettingRequest(string Value, string Reason);
public sealed record BackupDto(
    Guid Id, string Reference, BackupStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, long? SizeBytes, string? FailureMessage);
public sealed record RestoreBackupRequest(string Password, string TwoFactorCode, string WrittenReason);
public sealed record RevokeSessionRequest(string Reason);
public sealed record HealthSummaryDto(string Status, IReadOnlyDictionary<string, string> Components, DateTimeOffset CheckedAt);
