using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class RolePermission : EntityBase
{
    public Guid RoleId { get; set; }
    public required string Permission { get; set; }
    public Guid GrantedByUserId { get; set; }
}
