using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class SystemSetting : EntityBase
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
