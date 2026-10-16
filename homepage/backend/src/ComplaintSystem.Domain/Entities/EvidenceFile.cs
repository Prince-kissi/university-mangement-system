using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class EvidenceFile : SoftDeletableEntity
{
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid? ResponseId { get; set; }
    public ComplaintResponse? Response { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public required string OriginalFileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string StoragePath { get; set; }
    public required string Extension { get; set; }
    public required string MimeType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256Hash { get; set; }
    public MalwareScanStatus MalwareScanStatus { get; set; } = MalwareScanStatus.Pending;
}
