using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class ComplaintCategory : SoftDeletableEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public PriorityLevel DefaultPriority { get; set; } = PriorityLevel.Normal;
    public bool AllowsAnonymous { get; set; } = true;
    public bool IsActive { get; set; } = true;
}
