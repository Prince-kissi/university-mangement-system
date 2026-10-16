using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class Department : SoftDeletableEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ContactEmail { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Complaint> Complaints { get; set; } = [];
}
