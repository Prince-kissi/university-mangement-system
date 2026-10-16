using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class Complaint : SoftDeletableEntity
{
    public required string PublicId { get; set; }
    public Guid? StudentUserId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public Guid CategoryId { get; set; }
    public ComplaintCategory Category { get; set; } = null!;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public PriorityLevel Priority { get; set; } = PriorityLevel.Normal;
    public ComplaintStatus Status { get; set; } = ComplaintStatus.Submitted;
    public bool IsAnonymous { get; set; }
    public DateTimeOffset? FirstResponseDueAt { get; set; }
    public DateTimeOffset? ResolutionDueAt { get; set; }
    public DateTimeOffset? FirstRespondedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int EscalationLevel { get; set; }
    public ICollection<ComplaintAssignment> Assignments { get; set; } = [];
    public ICollection<ComplaintStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<ComplaintResponse> Responses { get; set; } = [];
    public ICollection<InternalNote> InternalNotes { get; set; } = [];
    public ICollection<EvidenceFile> EvidenceFiles { get; set; } = [];
}
