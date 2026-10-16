using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class ActiveSession : EntityBase
{
    public Guid UserId { get; set; }
    public required string SessionTokenHash { get; set; }
    public required string IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
}
