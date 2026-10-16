using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class Notification : EntityBase
{
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public NotificationType Type { get; set; } = NotificationType.Information;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
