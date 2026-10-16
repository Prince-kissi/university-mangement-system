using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class ComplaintStatusHistory : EntityBase
{
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public ComplaintStatus? PreviousStatus { get; set; }
    public ComplaintStatus NewStatus { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public required string Reason { get; set; }
    public string? MetadataJson { get; set; }
}
