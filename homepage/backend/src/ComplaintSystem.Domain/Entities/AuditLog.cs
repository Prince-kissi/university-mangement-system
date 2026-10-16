using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class AuditLog : EntityBase
{
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string RelatedRecordType { get; set; }
    public string? RelatedRecordId { get; set; }
    public required string IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Succeeded { get; set; }
    public required string MetadataJson { get; set; }
}
