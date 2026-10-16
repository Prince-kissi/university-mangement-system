using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class StudentVerification : EntityBase
{
    public Guid StudentId { get; set; }
    public required string Method { get; set; }
    public required string Destination { get; set; }
    public string? CodeHash { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public int ResendCount { get; set; }
    public DateTimeOffset? LastSentAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
