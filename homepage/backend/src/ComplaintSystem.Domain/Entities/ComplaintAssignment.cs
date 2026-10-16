using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class ComplaintAssignment : EntityBase
{
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid OfficerUserId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? UnassignedAt { get; set; }
    public string? Reason { get; set; }
    public bool IsCurrent { get; set; } = true;
}
