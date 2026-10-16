using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class InternalNote : EntityBase
{
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public required string Body { get; set; }
}
