using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class BackupRecord : EntityBase
{
    public required string Reference { get; set; }
    public Guid InitiatedByUserId { get; set; }
    public BackupStatus Status { get; set; } = BackupStatus.Requested;
    public string? StorageReference { get; set; }
    public long? SizeBytes { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? RestoredAt { get; set; }
    public Guid? RestoredByUserId { get; set; }
    public string? RestoreReason { get; set; }
    public string? FailureMessage { get; set; }
}
