using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class EvidenceAccessLog : EntityBase
{
    public Guid EvidenceFileId { get; set; }
    public EvidenceFile EvidenceFile { get; set; } = null!;
    public Guid AccessedByUserId { get; set; }
    public EvidenceAccessType AccessType { get; set; }
    public required string IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
